﻿using System.Linq;
using System.Threading.Tasks;
using Raven.Tests.Core;
using Raven.Tests.Core.Utils.Entities;
using Xunit;

namespace FastTests.Server.Queries
{
    public class BasicDynamicQueriesTests : RavenTestBase
    {
        [Fact]
        public async Task Dynamic_query_with_simple_where_clause()
        {
            using (var store = await GetDocumentStore())
            {
                using (var session = store.OpenAsyncSession())
                {
                    await session.StoreAsync(new User { Name = "Fitzchak" });
                    await session.StoreAsync(new User { Name = "Arek" });

                    await session.SaveChangesAsync();
                }
                // TODO arek: it occassionally fails with: Lucene.Net.Store.LockObtainFailedException: Lock obtain timed out: Lucene.Net.Store.SingleInstanceLock: write.lock
                using (var session = store.OpenSession())
                {
                    var users = session.Query<User>().Where(x => x.Name == "Arek").ToList();

                    Assert.Equal(1, users.Count);
                    Assert.Equal("Arek", users[0].Name);
                }
            }
        }

    }
}