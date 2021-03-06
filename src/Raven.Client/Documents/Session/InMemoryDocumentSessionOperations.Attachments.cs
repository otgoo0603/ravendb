//-----------------------------------------------------------------------
// <copyright file="InMemoryDocumentSessionOperations.cs" company="Hibernating Rhinos LTD">
//     Copyright (c) Hibernating Rhinos LTD. All rights reserved.
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.IO;
using Raven.Client.Documents.Commands.Batches;
using Raven.Client.Documents.Operations;
using Raven.Client.Json.Converters;
using Sparrow.Json;

namespace Raven.Client.Documents.Session
{
    /// <summary>
    /// Abstract implementation for in memory session operations
    /// </summary>
    public abstract partial class InMemoryDocumentSessionOperations
    {
        public AttachmentName[] GetAttachmentNames(object entity)
        {
            if (entity == null ||
                DocumentsByEntity.TryGetValue(entity, out DocumentInfo document) == false ||
                document.Metadata.TryGet(Constants.Documents.Metadata.Attachments, out BlittableJsonReaderArray attachments) == false)
                return Array.Empty<AttachmentName>();

            var results = new AttachmentName[attachments.Length];
            for (var i = 0; i < attachments.Length; i++)
            {
                var attachment = (BlittableJsonReaderObject)attachments[i];
                results[i] = JsonDeserializationClient.AttachmentName(attachment);
            }
            return results;
        }

        public void StoreAttachment(string documentId, string name, Stream stream, string contentType = null)
        {
            if (string.IsNullOrWhiteSpace(documentId))
                throw new ArgumentNullException(nameof(documentId));
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentNullException(nameof(name));

            if (DeferredCommandsDictionary.ContainsKey((documentId, CommandType.DELETE, null)))
                throw new InvalidOperationException($"Can't store attachment {name} of document {documentId}, there is a deferred command registered for this document to be deleted.");

            if (DeferredCommandsDictionary.ContainsKey((documentId, CommandType.AttachmentPUT, name)))
                throw new InvalidOperationException($"Can't store attachment {name} of document {documentId}, there is a deferred command registered to create an attachment with the same name.");

            if (DeferredCommandsDictionary.ContainsKey((documentId, CommandType.AttachmentDELETE, name)))
                throw new InvalidOperationException($"Can't store attachment {name} of document {documentId}, there is a deferred command registered to delete an attachment with the same name.");

            if (DocumentsById.TryGetValue(documentId, out DocumentInfo documentInfo) &&
                DeletedEntities.Contains(documentInfo.Entity))
                throw new InvalidOperationException($"Can't store attachment {name} of document {documentId}, the document was already deleted in this session.");

            Defer(new PutAttachmentCommandData(documentId, name, stream, contentType, null));
        }

        public void StoreAttachment(object entity, string name, Stream stream, string contentType = null)
        {
            if (DocumentsByEntity.TryGetValue(entity, out DocumentInfo document) == false)
                ThrowEntityNotInSession(entity);

            StoreAttachment(document.Id, name, stream, contentType);
        }

        protected void ThrowEntityNotInSession(object entity)
        {
            throw new ArgumentException(entity + " is not associated with the session, cannot add attachment to it. " +
                                        "Use documentId instead or track the entity in the session.", nameof(entity));
        }

        public void DeleteAttachment(object entity, string name)
        {
            if (DocumentsByEntity.TryGetValue(entity, out DocumentInfo document) == false)
                ThrowEntityNotInSession(entity);

            DeleteAttachment(document.Id, name);
        }

        public void DeleteAttachment(string documentId, string name)
        {
            if (string.IsNullOrWhiteSpace(documentId))
                throw new ArgumentNullException(nameof(documentId));
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentNullException(nameof(name));

            if (DeferredCommandsDictionary.ContainsKey((documentId, CommandType.DELETE, null)) ||
                DeferredCommandsDictionary.ContainsKey((documentId, CommandType.AttachmentDELETE, name)))
                return; // no-op

            if (DocumentsById.TryGetValue(documentId, out DocumentInfo documentInfo) &&
                DeletedEntities.Contains(documentInfo.Entity))
                return; // no-op

            if (DeferredCommandsDictionary.ContainsKey((documentId, CommandType.AttachmentPUT, name)))
                throw new InvalidOperationException($"Can't delete attachment {name} of document {documentId}, there is a deferred command registered to create an attachment with the same name.");

            Defer(new DeleteAttachmentCommandData(documentId, name, null));
        }
    }
}