﻿using System.Threading.Tasks;
using Raven.Client.Server.ETL;
using Raven.Client.Server.Operations;
using Raven.Client.Server.Operations.ETL;
using Xunit;

namespace SlowTests.Server.Documents.ETL
{
    public class EtlTasksManagement_RavenDB_7276 : EtlTestBase
    {
        [Fact]
        public async Task CanDeleteEtl()
        {
            using (var store = GetDocumentStore())
            {
                var database = await GetDatabase(store.Database);

                var configuration = new RavenEtlConfiguration()
                {
                    ConnectionStringName = "test",
                    Name = "aaa",
                    Transforms =
                    {
                        new Transformation()
                        {
                            Collections = {"Users"}
                        }
                    }
                };

                AddEtl(store, configuration, new RavenConnectionString()
                {
                    Name = "test",
                    Url = "http://127.0.0.1:8080",
                    Database = "Northwind",
                });

                store.Admin.Server.Send(new DeleteOngoingTaskOperation(database.Name, configuration.Id, OngoingTaskType.RavenEtl));
            }
        }

        [Fact]
        public async Task CanUpdateEtl()
        {
            using (var store = GetDocumentStore())
            {
                var database = await GetDatabase(store.Database);

                var configuration = new RavenEtlConfiguration()
                {
                    ConnectionStringName = "test",
                    Name = "aaa",
                    Transforms =
                    {
                        new Transformation()
                        {
                            Collections = {"Users"}
                        }
                    }
                };

                AddEtl(store, configuration, new RavenConnectionString()
                {
                    Name = "test",
                    Url = "http://127.0.0.1:8080",
                    Database = "Northwind",
                });

                var id = configuration.Id;

                configuration.Transforms[0].Disabled = true;

                store.Admin.Server.Send(new UpdateEtlOperation<RavenConnectionString>(id, configuration, database.Name));
            }
        }

        [Fact]
        public async Task CanDisableEtl()
        {
            using (var store = GetDocumentStore())
            {
                var database = await GetDatabase(store.Database);

                var configuration = new RavenEtlConfiguration()
                {
                    ConnectionStringName = "test",
                    Name = "aaa",
                    Transforms =
                    {
                        new Transformation()
                        {
                            Collections = {"Users"}
                        }
                    }
                };

                AddEtl(store, configuration, new RavenConnectionString()
                {
                    Name = "test",
                    Url = "http://127.0.0.1:8080",
                    Database = "Northwind",
                });


                store.Admin.Server.Send(new ToggleTaskStateOperation(database.Name , configuration.Id, OngoingTaskType.RavenEtl, true));
            }
        }
    }
}