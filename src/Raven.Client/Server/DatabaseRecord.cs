﻿using System.Collections.Generic;
using Raven.Client.Documents;
using Raven.Client.Documents.Exceptions.Indexes;
using Raven.Client.Documents.Indexes;
using Raven.Client.Documents.Transformers;
using Raven.Server.Documents.Versioning;

namespace Raven.Client.Server
{
    public class DatabaseRecord
    {
        public DatabaseRecord()
        {
            
        }

        public DatabaseRecord(string databaseName)
        {
            DatabaseName = databaseName;
        }

        public string DatabaseName;

        public bool Disabled;

        public Dictionary<string, DeletionInProgressStatus> DeletionInProgress;

        public string DataDirectory;

        public DatabaseTopology Topology;

        public Dictionary<string, IndexDefinition> Indexes;

        public Dictionary<string, AutoIndexDefinition> AutoIndexes;

        //todo: see how we can protect this
        public Dictionary<string, TransformerDefinition> Transformers;

        public Dictionary<string, string> Settings;

        public Dictionary<string, string> SecuredSettings { get; set; }

        public VersioningConfiguration VersioningConfiguration;

        public void AddIndex(IndexDefinition definition)
        {
            if (Transformers != null && Transformers.ContainsKey(definition.Name))
            {
                throw new IndexOrTransformerAlreadyExistException($"Tried to create an index with a name of {definition.Name}, but a transformer under the same name exists");
            }

            var lockMode = IndexLockMode.Unlock;

            IndexDefinition existingDefinition;
            if (Indexes.TryGetValue(definition.Name, out existingDefinition) && existingDefinition.LockMode != null)
                lockMode = existingDefinition.LockMode.Value;

            if (lockMode == IndexLockMode.LockedIgnore)
                return;

            if (lockMode == IndexLockMode.LockedError)
                throw new IndexOrTransformerAlreadyExistException($"Cannot edit existing index {definition.Name} with lock mode {lockMode}");

            Indexes[definition.Name] = definition;
        }

        public void AddIndex(AutoIndexDefinition definition)
        {
            if (Transformers != null && Transformers.ContainsKey(definition.Name))
            {
                throw new IndexOrTransformerAlreadyExistException($"Tried to create an index with a name of {definition.Name}, but a transformer under the same name exists");
            }

            var lockMode = IndexLockMode.Unlock;

            AutoIndexDefinition existingDefinition;
            if (AutoIndexes.TryGetValue(definition.Name, out existingDefinition) && existingDefinition.LockMode != null)
                lockMode = existingDefinition.LockMode.Value;

            if (lockMode == IndexLockMode.LockedIgnore)
                return;

            if (lockMode == IndexLockMode.LockedError)
                throw new IndexOrTransformerAlreadyExistException($"Cannot edit existing index {definition.Name} with lock mode {lockMode}");

            AutoIndexes[definition.Name] = definition;
        }

        public void AddTransformer(TransformerDefinition definition)
        {
            if (Indexes != null && Indexes.ContainsKey(definition.Name))
            {
                throw new IndexOrTransformerAlreadyExistException($"Tried to create a transformer with a name of {definition.Name}, but an index under the same name exists");
            }

            TransformerDefinition existingTransformer;
            var lockMode = TransformerLockMode.Unlock;
            if (Transformers.TryGetValue(definition.Name, out existingTransformer))
                lockMode = existingTransformer.LockMode;

            if (lockMode == TransformerLockMode.LockedIgnore)
                return;

            if (lockMode == TransformerLockMode.LockedError)
                throw new IndexOrTransformerAlreadyExistException($"Cannot edit existing transformer {definition.Name} with lock mode {lockMode}");

            Transformers[definition.Name] = definition;
        }

        public void DeleteIndex(string name)
        {
            Indexes?.Remove(name);
            AutoIndexes?.Remove(name);
        }

        public void DeleteTransformer(string name)
        {
            Transformers?.Remove(name);
        }
    }

    public enum DeletionInProgressStatus
    {
        No,
        SoftDelete,
        HardDelete
    }
}
