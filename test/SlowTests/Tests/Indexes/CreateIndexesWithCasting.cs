using System.Linq;
using FastTests;
using FastTests.Server.Basic.Entities;
using Raven.Client.Document;
using Raven.Client.Indexes;
using Raven.Tests.Core.Utils.Entities;
using Xunit;

namespace SlowTests.Tests.Indexes
{
    public class CreateIndexesWithCasting : RavenNewTestBase
    {
        [Fact]
        public void WillPreserverTheCasts()
        {
            var indexDefinition = new WithCasting
            {
                Conventions = new DocumentConventions { PrettifyGeneratedLinqExpressions = false }
            }.CreateIndexDefinition();

            var map = indexDefinition.Maps.First();

            Assert.Contains("docs.People.Select(person => new {", map);
            Assert.Contains("Id = ((long) person.Name.Length)", map);
        }

        public class WithCasting : AbstractIndexCreationTask<Person>
        {
            public WithCasting()
            {
                Map = persons => from person in persons
                                 select new { Id = (long)person.Name.Length };
            }
        }
    }
}