// -----------------------------------------------------------------------
//  <copyright file="RavenDB_295.cs" company="Hibernating Rhinos LTD">
//      Copyright (c) Hibernating Rhinos LTD. All rights reserved.
//  </copyright>
// -----------------------------------------------------------------------

using System.Collections.Generic;
using FastTests;
using Raven.Client.Indexing;
using Raven.Client.Operations.Databases.Indexes;
using Xunit;

namespace SlowTests.Issues
{
    public class RavenDB_295 : RavenNewTestBase
    {
        [Fact(Skip = "Missing feature: Suggestions")]
        public void CanUpdateSuggestions()
        {
            using (var store = GetDocumentStore())
            {
                using (var session = store.OpenSession())
                {
                    session.Store(new { Name = "john" });
                    session.Store(new { Name = "darsy" });
                    session.SaveChanges();
                }
                store.Admin.Send(new PutIndexOperation("test",
                    new IndexDefinition
                    {
                        Maps = new HashSet<string> { "from doc in docs select new { doc.Name}" },
                        Fields = new Dictionary<string, IndexFieldOptions>
                        {
                            {
                                "Name",
                                new IndexFieldOptions {Suggestions = true}
                            }
                        }
                    }));

                WaitForIndexing(store);

                //var suggestionQueryResult = store.DatabaseCommands.Suggest("test", new SuggestionQuery
                //{
                //    Field = "Name",
                //    Term = "orne"
                //});
                //Assert.Empty(suggestionQueryResult.Suggestions);

                using (var session = store.OpenSession())
                {
                    session.Store(new { Name = "oren" });
                    session.SaveChanges();
                }
                WaitForIndexing(store);

                //suggestionQueryResult = store.DatabaseCommands.Suggest("test", new SuggestionQuery
                //{
                //    Field = "Name",
                //    Term = "orne"
                //});
                //Assert.NotEmpty(suggestionQueryResult.Suggestions);
            }
        }

        [Fact(Skip = "Missing feature: Suggestions")]
        public void CanUpdateSuggestions_AfterRestart()
        {
            var dataDir = NewDataPath();
            using (var store = GetDocumentStore(path: dataDir))
            {
                using (var session = store.OpenSession())
                {
                    session.Store(new { Name = "john" });
                    session.Store(new { Name = "darsy" });
                    session.SaveChanges();
                }
                store.Admin.Send(new PutIndexOperation("test", new IndexDefinition
                {
                    Maps = new HashSet<string> { "from doc in docs select new { doc.Name}" },
                    Fields = new Dictionary<string, IndexFieldOptions>
                    {
                        {
                            "Name",
                            new IndexFieldOptions {Suggestions = true}
                        }
                    }
                }));

                WaitForIndexing(store);

                //var suggestionQueryResult = store.DatabaseCommands.Suggest("test", new SuggestionQuery
                //{
                //    Field = "Name",
                //    Term = "jhon"
                //});
                //Assert.NotEmpty(suggestionQueryResult.Suggestions);
            }

            using (var store = GetDocumentStore(path: dataDir))
            {
                using (var session = store.OpenSession())
                {
                    session.Store(new { Name = "oren" });
                    session.SaveChanges();
                }
                WaitForIndexing(store);

                //var suggestionQueryResult = store.DatabaseCommands.Suggest("test", new SuggestionQuery
                //{
                //    Field = "Name",
                //    Term = "jhon"
                //});
                //Assert.NotEmpty(suggestionQueryResult.Suggestions);

                //suggestionQueryResult = store.DatabaseCommands.Suggest("test", new SuggestionQuery
                //{
                //    Field = "Name",
                //    Term = "orne"
                //});
                //Assert.NotEmpty(suggestionQueryResult.Suggestions);
            }
        }
    }
}