﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using Raven.Client.Documents.Changes;
using Raven.Client.Documents.Exceptions.Transformers;
using Raven.Client.Documents.Transformers;
using Raven.Client.Exceptions.Cluster;
using Raven.Client.Server;
using Raven.Server.Config.Settings;
using Raven.Server.Documents.Indexes.Static;
using Raven.Server.NotificationCenter.Notifications;
using Raven.Server.NotificationCenter.Notifications.Details;
using Raven.Server.ServerWide;
using Raven.Server.ServerWide.Commands;
using Raven.Server.ServerWide.Commands.Transformers;
using Raven.Server.ServerWide.Context;
using Sparrow.Logging;

namespace Raven.Server.Documents.Transformers
{
    public class TransformerStore : IDisposable
    {
        private readonly Logger _log;

        private readonly DocumentDatabase _documentDatabase;
        private readonly ServerStore _serverStore;

        private readonly CollectionOfTransformers _transformers = new CollectionOfTransformers();

        /// <summary>
        /// The current lock, used to make sure indexes/transformers have a unique names
        /// </summary>
        private readonly object _indexAndTransformerLocker;

        private bool _initialized;

        private PathSetting _path;

        public TransformerStore(DocumentDatabase documentDatabase, ServerStore serverStore, object indexAndTransformerLocker)
        {
            _documentDatabase = documentDatabase;
            _serverStore = serverStore;
            _log = LoggingSource.Instance.GetLogger<TransformerStore>(_documentDatabase.Name);
            _indexAndTransformerLocker = indexAndTransformerLocker;

            _serverStore.Cluster.DatabaseChanged += OnDatabaseChanged;
        }

        private void OnDatabaseChanged(object sender, string databaseName)
        {
            if (string.Equals(databaseName, _documentDatabase.Name, StringComparison.OrdinalIgnoreCase) == false)
                return;

            try
            {
                lock (_indexAndTransformerLocker)
                {
                    TransactionOperationContext context;
                    using (_serverStore.ContextPool.AllocateOperationContext(out context))
                    {
                        DatabaseRecord record;
                        using (context.OpenReadTransaction())
                        {
                            record = _serverStore.Cluster.ReadDatabase(context, _documentDatabase.Name);
                            if (record == null)
                                return;
                        }

                        lock (_indexAndTransformerLocker)
                        {
                            HandleDeletes(record);
                            HandleChanges(record);
                        }
                    }
                }
            }
            catch (Exception e)
            {
                // log here and continue?
            }
        }

        private void HandleChanges(DatabaseRecord record)
        {
            foreach (var kvp in record.Transformers)
            {
                var name = kvp.Key;
                var definition = kvp.Value;

                var existingTransformer = GetTransformer(name);
                if (existingTransformer != null)
                {
                    if (definition.Equals(existingTransformer.Definition))
                        continue;

                    DeleteTransformerInternal(existingTransformer);
                }

                var transformer = Transformer.CreateNew(definition, _documentDatabase.Configuration.Indexing, LoggingSource.Instance.GetLogger<Transformer>(_documentDatabase.Name));
                CreateTransformerInternal(transformer);
            }
        }

        private void HandleDeletes(DatabaseRecord record)
        {
            foreach (var transformer in _transformers)
            {
                if (record.Transformers.ContainsKey(transformer.Name))
                    continue;

                DeleteTransformerInternal(transformer);
            }
        }

        public Task InitializeAsync(DatabaseRecord record)
        {
            if (_initialized)
                throw new InvalidOperationException($"{nameof(TransformerStore)} was already initialized.");

            lock (_indexAndTransformerLocker)
            {
                if (_initialized)
                    throw new InvalidOperationException($"{nameof(TransformerStore)} was already initialized.");

                _initialized = true;

                return Task.Factory.StartNew(() => OpenTransformers(record), TaskCreationOptions.LongRunning);
            }
        }

        private void OpenTransformers(DatabaseRecord record)
        {
            if (_documentDatabase.Configuration.Indexing.RunInMemory)
                return;

            lock (_indexAndTransformerLocker)
            {
                foreach (var kvp in record.Transformers)
                {
                    if (_documentDatabase.DatabaseShutdown.IsCancellationRequested)
                        return;

                    var definition = kvp.Value;

                    List<Exception> exceptions = null;
                    if (_documentDatabase.Configuration.Core.ThrowIfAnyIndexOrTransformerCouldNotBeOpened)
                        exceptions = new List<Exception>();

                    try
                    {
                        var transformer = Transformer.CreateNew(definition, _documentDatabase.Configuration.Indexing, LoggingSource.Instance.GetLogger<Transformer>(_documentDatabase.Name));
                        _transformers.Add(transformer);
                    }
                    catch (Exception e)
                    {
                        exceptions?.Add(e);

                        var fakeTransformer = new FaultyInMemoryTransformer(kvp.Key, definition.Etag, e);

                        var message = $"Could not open transformer with etag {definition.Etag}. Created in-memory, fake instance: {fakeTransformer.Name}";

                        if (_log.IsOperationsEnabled)
                            _log.Operations(message, e);

                        _documentDatabase.NotificationCenter.Add(AlertRaised.Create("Transformers store initialization error",
                            message,
                            AlertType.TransformerStore_TransformerCouldNotBeOpened,
                            NotificationSeverity.Error,
                            key: fakeTransformer.Name,
                            details: new ExceptionDetails(e)));

                        _transformers.Add(fakeTransformer);
                    }

                    if (exceptions != null && exceptions.Count > 0)
                        throw new AggregateException("Could not load some of the transformers", exceptions);
                }
            }
        }

        public async Task<long> CreateTransformer(TransformerDefinition definition)
        {
            if (definition == null)
                throw new ArgumentNullException(nameof(definition));

            IndexAndTransformerCompilationCache.GetTransformerInstance(definition); // pre-compile it and validate

            var command = new PutTransformerCommand(definition, _documentDatabase.Name);

            try
            {
                var index = await _serverStore.SendToLeaderAsync(command);

                await _serverStore.Cluster.WaitForIndexNotification(index);

                return index;
            }
            catch (CommandExecutionException e)
            {
                throw e.InnerException;
            }
        }

        public Transformer GetTransformer(int id)
        {
            Transformer transformer;
            if (_transformers.TryGetByEtag(id, out transformer) == false)
                return null;

            return transformer;
        }

        public Transformer GetTransformer(string name)
        {
            Transformer transformer;
            if (_transformers.TryGetByName(name, out transformer) == false)
                return null;

            return transformer;
        }

        public async Task<bool> TryDeleteTransformerIfExists(string name)
        {
            var transformer = GetTransformer(name);
            if (transformer == null)
                return false;

            var etag = await _serverStore.SendToLeaderAsync(new DeleteTransformerCommand(transformer.Name, _documentDatabase.Name));

            await _serverStore.Cluster.WaitForIndexNotification(etag);

            return true;
        }

        public async Task DeleteTransformer(string name)
        {
            var transformer = GetTransformer(name);
            if (transformer == null)
                TransformerDoesNotExistException.ThrowFor(name);

            var etag = await _serverStore.SendToLeaderAsync(new DeleteTransformerCommand(transformer.Name, _documentDatabase.Name));

            await _serverStore.Cluster.WaitForIndexNotification(etag);
        }

        public IEnumerable<Transformer> GetTransformers()
        {
            return _transformers;
        }

        public int GetTransformersCount()
        {
            return _transformers.Count;
        }

        public async Task SetLock(string name, TransformerLockMode mode)
        {
            var transformer = GetTransformer(name);
            if (transformer == null)
                TransformerDoesNotExistException.ThrowFor(name);

            var command = new SetTransformerLockCommand(name, mode, _documentDatabase.Name);

            var etag = await _serverStore.SendToLeaderAsync(command);

            await _serverStore.Cluster.WaitForIndexNotification(etag);
        }

        public async Task Rename(string name, string newName)
        {
            var transformer = GetTransformer(name);
            if (transformer == null)
                TransformerDoesNotExistException.ThrowFor(name);

            var command = new RenameTransformerCommand(name, newName, _documentDatabase.Name);

            var etag = await _serverStore.SendToLeaderAsync(command);

            await _serverStore.Cluster.WaitForIndexNotification(etag);
        }

        private void DeleteTransformerInternal(Transformer transformer)
        {
            lock (_indexAndTransformerLocker)
            {
                Transformer _;
                _transformers.TryRemoveByEtag(transformer.Etag, out _);

                _documentDatabase.Changes.RaiseNotifications(new TransformerChange
                {
                    Name = transformer.Name,
                    Type = TransformerChangeTypes.TransformerRemoved,
                    Etag = transformer.Etag
                });
            }
        }

        private void CreateTransformerInternal(Transformer transformer)
        {
            Debug.Assert(transformer != null);
            Debug.Assert(transformer.Etag > 0);

            _transformers.Add(transformer);
            _documentDatabase.Changes.RaiseNotifications(new TransformerChange
            {
                Name = transformer.Name,
                Type = TransformerChangeTypes.TransformerAdded,
                Etag = transformer.Etag
            });
        }

        public void Dispose()
        {
            _serverStore.Cluster.DatabaseChanged -= OnDatabaseChanged;
        }
    }
}