﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Raven.Server.Rachis;
using Raven.Server.ServerWide.Context;
using Sparrow.Json;
using Sparrow.Json.Parsing;
using Tests.Infrastructure;
using Xunit;

namespace SlowTests.Server.Rachis
{
    public class CommandsTests: RachisConsensusTestBase
    {
        [Fact]
        public async Task When_command_committed_CompletionTaskSource_is_notified()
        {
            const int commandCount = 10;
            const int clusterSize = 3;
            var leader = await CreateNetworkAndGetLeader(clusterSize);
            var nonLeader = GetFirstNonLeader();
            var tasks = new List<Task>();
            long lastIndex;
            TransactionOperationContext context;
            using (leader.ContextPool.AllocateOperationContext(out context))
            {
                for (var i = 0; i < commandCount; i++)
                {
                    tasks.Add(leader.PutAsync(context.ReadObject(new DynamicJsonValue
                    {
                        ["Name"] = "test",
                        ["Value"] = i
                    }, "test")));
                }
                using (context.OpenReadTransaction())
                    lastIndex = leader.GetLastEntryIndex(context);
            }
            var waitForAllCommits = nonLeader.WaitForCommitIndexChange(RachisConsensus.CommitIndexModification.GreaterOrEqual, lastIndex);
            Assert.True(await Task.WhenAny(waitForAllCommits, Task.Delay(5000)) == waitForAllCommits, "didn't commit in time");

            Assert.True(tasks.All(t=>t.Status == TaskStatus.RanToCompletion),"Some commands didn't complete");
        }

        [Fact]
        public async Task Command_not_committed_after_timeout_CompletionTaskSource_is_notified()
        {
            const int commandCount = 3;
            const int clusterSize = 3;
            var leader = await CreateNetworkAndGetLeader(clusterSize);
            var nonLeader = GetFirstNonLeader();
            var tasks = new List<Task>();
            long lastIndex;
            TransactionOperationContext context;
            using (leader.ContextPool.AllocateOperationContext(out context))
            {
                for (var i = 0; i < commandCount; i++)
                {
                    tasks.Add(leader.PutAsync(context.ReadObject(new DynamicJsonValue
                    {
                        ["Name"] = "test",
                        ["Value"] = i
                    }, "test")));
                }
                using (context.OpenReadTransaction())
                    lastIndex = leader.GetLastEntryIndex(context);
            }
            var waitForAllCommits = nonLeader.WaitForCommitIndexChange(RachisConsensus.CommitIndexModification.GreaterOrEqual, lastIndex);
            Assert.True(await Task.WhenAny(waitForAllCommits, Task.Delay(5000)) == waitForAllCommits, "didn't commit in time");

            Assert.True(tasks.All(t => t.Status == TaskStatus.RanToCompletion), "Some commands didn't complete");
            DisconnectFromNode(leader);
            using (leader.ContextPool.AllocateOperationContext(out context))
            {
                var aggregateException = Assert.Throws<AggregateException>( ()=> leader.PutAsync(context.ReadObject(new DynamicJsonValue
                {
                    ["Name"] = "test",
                    ["Value"] = commandCount
                }, "test")).Wait(leader.ElectionTimeoutMs*2));
                Assert.IsType<TaskCanceledException>(aggregateException.InnerException);
            }
        }
    }
}