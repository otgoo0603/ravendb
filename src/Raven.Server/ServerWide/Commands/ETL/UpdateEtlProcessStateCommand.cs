﻿using Raven.Client.Documents.Replication.Messages;
using Raven.Client.Json.Converters;
using Raven.Client.Server;
using Raven.Client.Server.ETL;
using Sparrow.Json;
using Sparrow.Json.Parsing;

namespace Raven.Server.ServerWide.Commands.ETL
{
    public class UpdateEtlProcessStateCommand : UpdateValueForDatabaseCommand
    {
        public string ConfigurationName { get; set; }

        public string TransformationName { get; set; }

        public long LastProcessedEtag { get; set; }

        public ChangeVectorEntry[] ChangeVector { get; set; }

        public string NodeTag { get; set; }

        private UpdateEtlProcessStateCommand() : base(null)
        {
            // for deserialization
        }

        public UpdateEtlProcessStateCommand(string databaseName, string configurationName, string transformationName, long lastProcessedEtag, ChangeVectorEntry[] changeVector,
            string nodeTag) : base(databaseName)
        {
            ConfigurationName = configurationName;
            TransformationName = transformationName;
            LastProcessedEtag = lastProcessedEtag;
            ChangeVector = changeVector;
            NodeTag = nodeTag;
        }

        public override string GetItemId()
        {
            return EtlProcessState.GenerateItemName(DatabaseName, ConfigurationName, TransformationName);
        }

        public override BlittableJsonReaderObject GetUpdatedValue(long index, DatabaseRecord record, JsonOperationContext context, BlittableJsonReaderObject existingValue, bool isPassive)
        {
            EtlProcessState state;

            if (existingValue != null)
                state = JsonDeserializationClient.EtlProcessState(existingValue);
            else
            {
                state = new EtlProcessState
                {
                    ConfigurationName = ConfigurationName,
                    TransformationName = TransformationName
                };
            }

            state.LastProcessedEtagPerNode[NodeTag] = LastProcessedEtag;
            state.ChangeVector = ChangeVector;


            return context.ReadObject(state.ToJson(), GetItemId());
        }

        public override void FillJson(DynamicJsonValue json)
        {
            json[nameof(ConfigurationName)] = ConfigurationName;
            json[nameof(TransformationName)] = TransformationName;
            json[nameof(LastProcessedEtag)] = LastProcessedEtag;
            json[nameof(NodeTag)] = NodeTag;
            json[nameof(ChangeVector)] = ChangeVector?.ToJson();
        }
    }
}