using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using Raven.NewClient.Abstractions.Extensions;
using Raven.NewClient.Abstractions.FileSystem;
using Raven.NewClient.Abstractions.FileSystem.Notifications;
using Raven.NewClient.Abstractions.Logging;
using Raven.NewClient.Client.Changes;
using Raven.NewClient.Client.Connection;
using Raven.NewClient.Json.Linq;

using Sparrow.Collections;

namespace Raven.NewClient.Client.FileSystem.Changes
{

    public class FilesChangesClient : RemoteChangesClientBase<IFilesChanges, FilesConnectionState, FilesConvention>, IFilesChanges
    {
        private readonly static ILog Logger = LogManager.GetLogger(typeof(FilesChangesClient));

        private readonly ConcurrentSet<string> watchedFolders = new ConcurrentSet<string>(StringComparer.OrdinalIgnoreCase);

        private bool watchAllConfigurations;
        private bool watchAllConflicts;
        private bool watchAllSynchronizations;

        private readonly Func<string, FileHeader, string, Action, Task<bool>> tryResolveConflictByUsingRegisteredConflictListenersAsync;

        public FilesChangesClient(string url, string apiKey,
                                       ICredentials credentials,
                                       HttpJsonRequestFactory jsonRequestFactory, FilesConvention conventions,
                                       Func<string, FileHeader, string, Action, Task<bool>> tryResolveConflictByUsingRegisteredConflictListenersAsync,
                                       Action onDispose)
            : base(url, apiKey, credentials, conventions, onDispose)
        {
            this.tryResolveConflictByUsingRegisteredConflictListenersAsync = tryResolveConflictByUsingRegisteredConflictListenersAsync;
        }

        public IObservableWithTask<ConfigurationChangeNotification> ForConfiguration()
        {
            var counter = GetOrAddConnectionState("all-fs-config", "watch-config", "unwatch-config",
                () => watchAllConfigurations = true,
                () => watchAllConfigurations = false,
                null);

            var taskedObservable = new TaskedObservable<ConfigurationChangeNotification, FilesConnectionState>(
                counter,
                notification => true);

            counter.OnConfigurationChangeNotification += taskedObservable.Send;
            counter.OnError += taskedObservable.Error;

            return taskedObservable;
        }

        public IObservableWithTask<ConflictNotification> ForConflicts()
        {
            var counter = GetOrAddConnectionState("all-fs-conflicts", "watch-conflicts", "unwatch-conflicts",
                () => watchAllConflicts = true,
                () => watchAllConflicts = false,
                null);

            var taskedObservable = new TaskedObservable<ConflictNotification, FilesConnectionState>(
                counter,
                notification => true);

            counter.OnConflictsNotification += taskedObservable.Send;
            counter.OnError += taskedObservable.Error;

            return taskedObservable;
        }

        public IObservableWithTask<FileChangeNotification> ForFolder(string folder)
        {
            if (string.IsNullOrWhiteSpace(folder))
                throw new ArgumentException("folder cannot be empty");

            if (!folder.StartsWith("/"))
                throw new ArgumentException("folder must start with /");

            var canonicalisedFolder = folder.TrimStart('/');
            var key = "fs-folder/" + canonicalisedFolder;
            var counter = GetOrAddConnectionState(key, "watch-folder", "unwatch-folder",
                () => watchedFolders.TryAdd(folder),
                () => watchedFolders.TryRemove(folder),
                folder);

            var taskedObservable = new TaskedObservable<FileChangeNotification, FilesConnectionState>(
                counter,
                notification => notification.File.StartsWith(folder, StringComparison.OrdinalIgnoreCase));

            counter.OnFileChangeNotification += taskedObservable.Send;
            counter.OnError += taskedObservable.Error;

            return taskedObservable;
        }

        public IObservableWithTask<SynchronizationUpdateNotification> ForSynchronization()
        {
            var counter = GetOrAddConnectionState("all-fs-sync", "watch-sync", "unwatch-sync",
                () => watchAllSynchronizations = true,
                () => watchAllSynchronizations = false,
                null);

            var taskedObservable = new TaskedObservable<SynchronizationUpdateNotification, FilesConnectionState>(
                counter,
                notification => true);

            counter.OnSynchronizationNotification += taskedObservable.Send;
            counter.OnError += taskedObservable.Error;

            return taskedObservable;
        }

        protected override async Task SubscribeOnServer()
        {
            if (watchAllConfigurations)
                await Send("watch-config", null).ConfigureAwait(false);

            if (watchAllConflicts)
                await Send("watch-conflicts", null).ConfigureAwait(false);

            if (watchAllSynchronizations)
                await Send("watch-sync", null).ConfigureAwait(false);

            foreach (var watchedFolder in watchedFolders)
            {
                await Send("watch-folder", watchedFolder).ConfigureAwait(false);
            }
        }

        private ConcurrentDictionary<string, ConflictNotification> delayedConflictNotifications = new ConcurrentDictionary<string, ConflictNotification>();

        protected override void NotifySubscribers(string type, RavenJObject value, List<FilesConnectionState> connections)
        {
            switch (type)
            {
                case "ConfigurationChangeNotification":
                    var configChangeNotification = value.JsonDeserialization<ConfigurationChangeNotification>();
                    foreach (var counter in connections)
                    {
                        counter.Send(configChangeNotification);
                    }
                    break;
                case "FileChangeNotification":
                    var fileChangeNotification = value.JsonDeserialization<FileChangeNotification>();
                    foreach (var counter in connections)
                    {
                        counter.Send(fileChangeNotification);
                    }
                    break;
                case "SynchronizationUpdateNotification":
                    var synchronizationUpdateNotification = value.JsonDeserialization<SynchronizationUpdateNotification>();
                    foreach (var counter in connections)
                    {
                        counter.Send(synchronizationUpdateNotification);
                    }
                    break;

                case "ConflictNotification":
                    var conflictNotification = value.JsonDeserialization<ConflictNotification>();
                    if (conflictNotification.Status == ConflictStatus.Detected)
                    {
                        // We don't care about this one (this can happen concurrently). 
                        delayedConflictNotifications.AddOrUpdate(conflictNotification.FileName, conflictNotification, (x, y) => conflictNotification);

                        tryResolveConflictByUsingRegisteredConflictListenersAsync(conflictNotification.FileName, 
                                                                                  conflictNotification.RemoteFileHeader, 
                                                                                  conflictNotification.SourceServerUrl,
                                                                                  () => NotifyConflictSubscribers(connections, conflictNotification))                             
                            .ContinueWith(t =>
                            {
                                t.AssertNotFailed();

                                // We need the lock to avoid a race conditions where a Detected happens and also a Resolved happen before the continuation can take control.. 
                                lock ( delayedConflictNotifications )
                                {
                                    ConflictNotification notification;
                                    if (delayedConflictNotifications.TryRemove(conflictNotification.FileName, out notification))
                                    {
                                        if (notification.Status == ConflictStatus.Resolved)
                                            NotifyConflictSubscribers(connections, notification);
                                    }
                                }

                                if (t.Result)
                                {
                                    if (Logger.IsDebugEnabled)
                                        Logger.Debug("Document replication conflict for {0} was resolved by one of the registered conflict listeners", conflictNotification.FileName);
                                }
                            }).ConfigureAwait(false);
                    }
                    else if (conflictNotification.Status == ConflictStatus.Resolved )
                    {
                        // We need the lock to avoid race conditions. 
                        lock ( delayedConflictNotifications )
                        {
                            if (delayedConflictNotifications.ContainsKey(conflictNotification.FileName))
                            {
                                delayedConflictNotifications.AddOrUpdate(conflictNotification.FileName, conflictNotification, (x, y) => conflictNotification);

                                // We are delaying broadcasting.
                                conflictNotification = null;
                            }
                            else NotifyConflictSubscribers(connections, conflictNotification);
                        }
                    }

                    break;
                default:
                    break;
            }
        }

        private static void NotifyConflictSubscribers(List<FilesConnectionState> connections, ConflictNotification conflictNotification)
        {
            // Check if we are delaying the broadcast.
            if (conflictNotification != null)
            {
                foreach (var counter in connections)
                    counter.Send(conflictNotification);
            }
        }
        }
    }