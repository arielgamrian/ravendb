﻿using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;
using System;
using Raven.Client.Blittable;
using Raven.Client.Document;
using Raven.Client.Util;
using Sparrow.Json;

namespace FastTests.Client
{
    public class IncludesUtilTests : RavenNewTestBase
    {
        [Fact]
        public void include_with_prefix()
        {
            using (var context = JsonOperationContext.ShortTermSingleUse())
            {
                var entityToBlittable = new EntityToBlittable(null);
                var json = entityToBlittable.ConvertEntityToBlittable(new Order
                {
                    CustomerId = "1",
                    Number = "abc"
                }, new DocumentConventions(), context);

                var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                IncludesUtil.Include(json, "CustomerId(customer/)", customerId =>
                {
                    if (customerId == null)
                        return false;
                    ids.Add(customerId);
                    return true;
                });

                Assert.Equal(new[] { "customer/1", "1" }, ids);
            }
        }

        [Fact]
        public void include_with_suffix()
        {
            using (var context = JsonOperationContext.ShortTermSingleUse())
            {
                var entityToBlittable = new EntityToBlittable(null);
                var json = entityToBlittable.ConvertEntityToBlittable(new Order
                {
                    CustomerId = "1",
                    Number = "abc"
                }, new DocumentConventions(), context);

                var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                IncludesUtil.Include(json, "CustomerId[{0}/customer]", customerId =>
                {
                    if (customerId == null)
                        return false;
                    ids.Add(customerId);
                    return true;
                });

                Assert.Equal(new[] { "1/customer", "1" }, ids);
            }
        }

        private class Order
        {
            public string Number { get; set; }
            public string CustomerId { get; set; }
        }
    }
}