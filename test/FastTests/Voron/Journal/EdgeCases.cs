﻿// -----------------------------------------------------------------------
//  <copyright file="EdgeCases.cs" company="Hibernating Rhinos LTD">
//      Copyright (c) Hibernating Rhinos LTD. All rights reserved.
//  </copyright>
// -----------------------------------------------------------------------

using System;
using System.IO;
using Xunit;
using Voron;
using Voron.Global;

namespace FastTests.Voron.Journal
{
    public class EdgeCases : StorageTest
    {
        // all tests here relay on the fact than one log file can contains max 10 pages 
        protected override void Configure(StorageEnvironmentOptions options)
        {
            options.ManualFlushing = true;
            options.MaxLogFileSize = 5 * Constants.Storage.PageSize;
        }

        [Fact]
        public void TransactionCommitShouldSetCurrentLogFileToNullIfItIsFull()
        {
            using (var tx = Env.WriteTransaction())
            {
                var tree = tx.CreateTree("foo");
                var bytes = new byte[4 * Constants.Storage.PageSize];
                new Random().NextBytes(bytes);
                tree.Add("items/0", new MemoryStream(bytes));
                tx.Commit();
            }

            using (var tx = Env.WriteTransaction())
            {
                var tree = tx.CreateTree("foo");
                var bytes = new byte[4 * Constants.Storage.PageSize];
                new Random().NextBytes(bytes);
                tree.Add("items/1", new MemoryStream(bytes));
                tx.Commit();
            }

            using (var tx = Env.WriteTransaction())
            {
                var tree = tx.CreateTree("foo");
                var bytes = new byte[4 * Constants.Storage.PageSize];
                new Random().NextBytes(bytes);
                tree.Add("items/1", new MemoryStream(bytes));
                tx.Commit();
            }

            using (var tx = Env.WriteTransaction())
            {
                var tree = tx.CreateTree("foo");
                var bytes = new byte[4 * Constants.Storage.PageSize];
                new Random().NextBytes(bytes);
                tree.Add("items/1", new MemoryStream(bytes));
                tx.Commit();
            }

            Assert.Null(Env.Journal.CurrentFile);
            Assert.Equal(5, Env.Journal.Files.Count);
        }
    }
}