//-----------------------------------------------------------------------
// <copyright file="ExpiredDocumentsCleaner.cs" company="Hibernating Rhinos LTD">
//     Copyright (c) Hibernating Rhinos LTD. All rights reserved.
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Threading.Tasks;
using Raven.Client;
using Raven.Client.Exceptions.Documents;
using Raven.Client.ServerWide;
using Raven.Client.ServerWide.Expiration;
using Raven.Server.Background;
using Raven.Server.NotificationCenter.Notifications;
using Raven.Server.ServerWide.Context;
using Sparrow.Binary;
using Sparrow.Json;
using Sparrow.Logging;
using Voron;

namespace Raven.Server.Documents.Expiration
{
    public class ExpiredDocumentsCleaner : BackgroundWorkBase
    {
        private readonly DocumentDatabase _database;
        private readonly TimeSpan _period;

        private const string DocumentsByExpiration = "DocumentsByExpiration";

        public ExpirationConfiguration Configuration { get; }

        private ExpiredDocumentsCleaner(DocumentDatabase database, ExpirationConfiguration configuration) : base(database.Name, database.DatabaseShutdown)
        {
            Configuration = configuration;
            _database = database;

            var deleteFrequencyInSeconds = configuration.DeleteFrequencyInSec ?? 60;
            if (Logger.IsInfoEnabled)
                Logger.Info($"Initialized expired document cleaner, will check for expired documents every {deleteFrequencyInSeconds} seconds");

            _period = TimeSpan.FromSeconds(deleteFrequencyInSeconds);
        }

        public static ExpiredDocumentsCleaner LoadConfigurations(DocumentDatabase database, DatabaseRecord dbRecord, ExpiredDocumentsCleaner expiredDocumentsCleaner)
        {
            try
            {
                if (dbRecord.Expiration == null)
                {
                    expiredDocumentsCleaner?.Dispose();
                    return null;
                }
                if (dbRecord.Expiration.Equals(expiredDocumentsCleaner?.Configuration))
                    return expiredDocumentsCleaner;
                expiredDocumentsCleaner?.Dispose();
                if (dbRecord.Expiration.Active == false)
                    return null;

                var cleaner = new ExpiredDocumentsCleaner(database, dbRecord.Expiration);
                cleaner.Start();
                return cleaner;
            }
            catch (Exception e)
            {
                const string msg = "Cannot enable expired documents cleaner as the configuration record is not valid.";
                database.NotificationCenter.Add(AlertRaised.Create($"Expiration error in {database.Name}", msg,
                    AlertType.RevisionsConfigurationNotValid, NotificationSeverity.Error, database.Name));

                var logger = LoggingSource.Instance.GetLogger<ExpiredDocumentsCleaner>(database.Name);
                if (logger.IsOperationsEnabled)
                    logger.Operations(msg, e);

                return null;
            }
        }

        protected override async Task DoWork()
        {
            await WaitOrThrowOperationCanceled(_period);

            await CleanupExpiredDocs();
        }

        internal async Task CleanupExpiredDocs()
        {
            var currentTime = _database.Time.GetUtcNow();
            var currentTicks = currentTime.Ticks;

            try
            {
                if (Logger.IsInfoEnabled)
                    Logger.Info("Trying to find expired documents to delete");

                using (_database.DocumentsStorage.ContextPool.AllocateOperationContext(out DocumentsOperationContext context))
                {
                    using (var tx = context.OpenReadTransaction())
                    {
                        var expirationTree = tx.InnerTransaction.CreateTree(DocumentsByExpiration);

                        Dictionary<Slice, List<(Slice LowerId, LazyStringValue Id)>> expired;
                        Stopwatch duration;

                        using (var it = expirationTree.Iterate(false))
                        {
                            if (it.Seek(Slices.BeforeAllKeys) == false)
                                return;

                            expired = new Dictionary<Slice, List<(Slice LowerId, LazyStringValue Id)>>();
                            duration = Stopwatch.StartNew();

                            do
                            {
                                var entryTicks = it.CurrentKey.CreateReader().ReadBigEndianInt64();
                                if (entryTicks >= currentTicks)
                                    break;

                                var ticksAsSlice = it.CurrentKey.Clone(tx.InnerTransaction.Allocator);

                                var expiredDocs = new List<(Slice LowerId, LazyStringValue Id)>();

                                expired.Add(ticksAsSlice, expiredDocs);

                                using (var multiIt = expirationTree.MultiRead(it.CurrentKey))
                                {
                                    if (multiIt.Seek(Slices.BeforeAllKeys))
                                    {
                                        do
                                        {
                                            if (CancellationToken.IsCancellationRequested)
                                                return;

                                            var clonedId = multiIt.CurrentKey.Clone(tx.InnerTransaction.Allocator);

                                            try
                                            {
                                                var document = _database.DocumentsStorage.Get(context, clonedId);
                                                if (document == null)
                                                {
                                                    expiredDocs.Add((clonedId, null));
                                                    continue;
                                                }

                                                if (HasExpired(document.Data, currentTime) == false)
                                                    continue;

                                                expiredDocs.Add((clonedId, document.Id));
                                            }
                                            catch (DocumentConflictException)
                                            {
                                                LazyStringValue id = null;
                                                var allExpired = true;
                                                var conflicts = _database.DocumentsStorage.ConflictsStorage.GetConflictsFor(context, clonedId);
                                                if (conflicts.Count == 0)
                                                    continue;

                                                foreach (var conflict in conflicts)
                                                {
                                                    id = conflict.Id;

                                                    if (HasExpired(conflict.Doc, currentTime))
                                                        continue;

                                                    allExpired = false;
                                                    break;
                                                }

                                                if (allExpired)
                                                    expiredDocs.Add((clonedId, id));
                                            }
                                        } while (multiIt.MoveNext());
                                    }
                                }

                            } while (it.MoveNext());
                        }

                        var command = new DeleteExpiredDocumentsCommand(expired, _database, Logger);

                        await _database.TxMerger.Enqueue(command);

                        if (Logger.IsInfoEnabled)
                            Logger.Info($"Successfully deleted {command.DeletionCount:#,#;;0} documents in {duration.ElapsedMilliseconds:#,#;;0} ms.");
                    }
                }
            }
            catch (Exception e)
            {
                if (Logger.IsOperationsEnabled)
                    Logger.Operations($"Failed to delete expired documents on {_database.Name} which are older than {currentTime}", e);
            }
        }

        private static bool HasExpired(BlittableJsonReaderObject data, DateTime currentTime)
        {
            // Validate that the expiration value in metadata is still the same.
            // We have to check this as the user can update this value.
            if (data.TryGet(Constants.Documents.Metadata.Key, out BlittableJsonReaderObject metadata) == false ||
                metadata.TryGet(Constants.Documents.Metadata.Expires, out string expirationDate) == false)
                return false;

            if (DateTime.TryParseExact(expirationDate, "O", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var date) == false)
                return false;

            if (currentTime < date)
                return false;

            return true;
        }

        public unsafe void Put(DocumentsOperationContext context,
            Slice lowerId, BlittableJsonReaderObject document)
        {
            if (document.TryGet(Constants.Documents.Metadata.Key, out BlittableJsonReaderObject metadata) == false ||
                metadata.TryGet(Constants.Documents.Metadata.Expires, out string expirationDate) == false)
                return;

            if (DateTime.TryParseExact(expirationDate, "O", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTime date) == false)
                throw new InvalidOperationException($"The expiration date format is not valid: '{expirationDate}'. Use the following format: {_database.Time.GetUtcNow():O}");

            // We explicitly enable adding documents that have already been expired, we have to, because if the time lag is short, it is possible
            // that we add a document that expire in 1 second, but by the time we process it, it already expired. The user did nothing wrong here
            // and we'll use the normal cleanup routine to clean things up later.

            var ticksBigEndian = Bits.SwapBytes(date.Ticks);

            var tree = context.Transaction.InnerTransaction.CreateTree(DocumentsByExpiration);
            using (Slice.External(context.Allocator, (byte*)&ticksBigEndian, sizeof(long), out Slice ticksSlice))
                tree.MultiAdd(ticksSlice, lowerId);
        }

        private class DeleteExpiredDocumentsCommand : TransactionOperationsMerger.MergedTransactionCommand
        {
            private readonly Dictionary<Slice, List<(Slice LowerId, LazyStringValue Id)>> _expired;
            private readonly DocumentDatabase _database;
            private readonly Logger _logger;

            public int DeletionCount;

            public DeleteExpiredDocumentsCommand(Dictionary<Slice, List<(Slice LowerId, LazyStringValue Id)>> expired, DocumentDatabase database, Logger logger)
            {
                _expired = expired;
                _database = database;
                _logger = logger;
            }

            public override int Execute(DocumentsOperationContext context)
            {
                var expirationTree = context.Transaction.InnerTransaction.CreateTree(DocumentsByExpiration);

                foreach (var expired in _expired)
                {
                    foreach (var ids in expired.Value)
                    {
                        if (ids.Id != null)
                        {
                            var deleted = _database.DocumentsStorage.Delete(context, ids.LowerId, ids.Id, expectedChangeVector: null);

                            if (_logger.IsInfoEnabled && deleted == null)
                                _logger.Info($"Tried to delete expired document '{ids.Id}' but document was not found.");

                            DeletionCount++;
                        }

                        expirationTree.MultiDelete(expired.Key, ids.LowerId);
                    }
                }

                return DeletionCount;
            }
        }
    }
}
