﻿using System;
using System.IO;
using Raven.Client.Documents.Conventions;
using Sparrow.Json;
using Sparrow.Json.Parsing;

namespace Raven.Client.Documents.Commands.Batches
{
    public class PutAttachmentCommandData : ICommandData
    {
        public PutAttachmentCommandData(string documentId, string name, Stream stream, string contentType, long? etag)
        {
            if (string.IsNullOrWhiteSpace(documentId))
                throw new ArgumentNullException(nameof(documentId));
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentNullException(nameof(name));

            Key = documentId;
            Name = name;
            Stream = stream;
            ContentType = contentType;
            Etag = etag;
        }

        public string Key { get; }
        public string Name { get; }
        public Stream Stream { get; }
        public string ContentType { get; }
        public long? Etag { get; }
        public CommandType Type { get; } = CommandType.AttachmentPUT;

        public DynamicJsonValue ToJson(DocumentConventions conventions, JsonOperationContext context)
        {
            return new DynamicJsonValue
            {
                [nameof(Key)] = Key,
                [nameof(Name)] = Name,
                [nameof(ContentType)] = ContentType,
                [nameof(Etag)] = Etag,
                [nameof(Type)] = Type.ToString(),
            };
        }
    }
}