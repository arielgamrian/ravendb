using System.Linq;
using FastTests;
using Raven.Client.Indexing;
using Raven.Client.Operations.Databases.Indexes;
using Xunit;

namespace SlowTests.Bugs.Indexing
{
    public class FilterOnMissingProperty : RavenNewTestBase
    {
        [Fact]
        public void CanFilter()
        {
            using (var store = GetDocumentStore())
            {
                store.Admin.Send(new PutIndexOperation("test",
                    new IndexDefinition
                    {
                        Maps = { "from doc in docs where doc.Valid select new { doc.Name }" }
                    }));

                using (var session = store.OpenSession())
                {
                    session.Store(new { Valid = true, Name = "Oren" });

                    session.Store(new { Name = "Ayende " });

                    session.SaveChanges();
                }

                using (var session = store.OpenSession())
                {
                    session.Advanced.DocumentQuery<dynamic>("test").WaitForNonStaleResults().ToArray();
                }

                var db = GetDocumentDatabaseInstanceFor(store).Result;
                var errorsCount = db.IndexStore.GetIndexes().Sum(index => index.GetErrors().Count);

                Assert.Equal(errorsCount, 0);
            }
        }
    }
}