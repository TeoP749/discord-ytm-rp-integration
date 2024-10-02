using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Windows.Media.Control;
using Windows.Storage.Streams;

namespace MediaInfo
{
    class Program
    {
        private static Discord.Discord discord;
        private static Discord.ApplicationManager applicationManager;
        private static Discord.ActivityManager activityManager;
        private static Discord.LobbyManager lobbyManager;

        private static readonly System.Threading.Mutex _albumCoverLock = new();

        private static GlobalSystemMediaTransportControlsSessionManager sessionManager;
        private static GlobalSystemMediaTransportControlsSession currentSession;

        private static void BuildDiscord()
        {
            var clientID = Environment.GetEnvironmentVariable("DISCORD_CLIENT_ID");
            if (clientID == null)
            {
                clientID = "YOUR_CLIENT_ID_HERE";
            }

            discord = new Discord.Discord(Int64.Parse(clientID), (UInt64)Discord.CreateFlags.Default);
            discord.SetLogHook(Discord.LogLevel.Debug, (level, message) =>
            {
                Console.WriteLine("Log[{0}] {1}", level, message);
            });

            applicationManager = discord.GetApplicationManager();
            activityManager = discord.GetActivityManager();
            // activityManager.RegisterCommand("start chrome https://music.youtube.com");

            // activityManager.OnActivityJoin += (secret) =>
            // {
            //     Console.WriteLine("Joining {0}", secret);
            // };

            // activityManager.OnActivityJoinRequest += (ref Discord.User user) =>
            // {
            //     Console.WriteLine("OnJoinRequest {0} {1}", user.Username, user.Id);
            // };

            lobbyManager = discord.GetLobbyManager();

            // Get the current locale. This can be used to determine what text or audio the user wants.
            Console.WriteLine("Current Locale: {0}", applicationManager.GetCurrentLocale());
            // Get the current branch. For example alpha or beta.
            Console.WriteLine("Current Branch: {0}", applicationManager.GetCurrentBranch());
        }

        private static async void BuildMediaSession()
        {
            sessionManager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            sessionManager.CurrentSessionChanged += SessionManager_CurrentSessionChanged;
            currentSession = sessionManager.GetCurrentSession();
        }

        private static async Task Main(string[] args)
        {
            BuildDiscord();
            BuildMediaSession();
            if (currentSession != null)
            {
                currentSession.MediaPropertiesChanged += Session_MediaPropertiesChanged;
                currentSession.PlaybackInfoChanged += Session_PlaybackInfoChanged;
                currentSession.TimelinePropertiesChanged += Session_TimelinePropertiesChanged;
                await SongInfoChanged(true, false);
            }

            Task albumCoverServer = AlbumCoverServer.Serve(_albumCoverLock);

            // Keep the console application running
            Console.WriteLine("Listening for media changes...");
            try
            {
                while (true)
                {
                    discord.RunCallbacks();
                    lobbyManager.FlushNetwork();
                    Thread.Sleep(1000 / 60);
                }
            }
            finally
            {
                ClearActivity();
                discord.Dispose();
            }
        }

        private static void UpdateActivity(Discord.Activity activity)
        {
            activityManager.UpdateActivity(activity, result =>
            {
                if (result == Discord.Result.Ok)
                {
                    Console.WriteLine("INFO: updated activity.");
                }
                else
                {
                    Console.WriteLine("INFO: failed to update activity.");
                }
            });
        }

        private static void ClearActivity()
        {
            activityManager.ClearActivity(result =>
            {
                if (result == Discord.Result.Ok)
                {
                    Console.WriteLine("INFO: activity cleared.");
                }
                else
                {
                    Console.WriteLine("INFO: failed to clear activity.");
                }
            });
        }

        private static async void Session_MediaPropertiesChanged(GlobalSystemMediaTransportControlsSession sender, MediaPropertiesChangedEventArgs args)
        {
            System.Console.WriteLine("INFO: media properties changed");
            await SongInfoChanged(false, true);
        }

        private static async void Session_PlaybackInfoChanged(GlobalSystemMediaTransportControlsSession sender, PlaybackInfoChangedEventArgs args)
        {
            System.Console.WriteLine("INFO: playback info changed");
            await SongInfoChanged(false, false);
        }

        private static async void Session_TimelinePropertiesChanged(GlobalSystemMediaTransportControlsSession sender, TimelinePropertiesChangedEventArgs args)
        {
            System.Console.WriteLine("INFO: timeline properties changed");
            await SongInfoChanged(false, false);
        }

        private static void SessionManager_CurrentSessionChanged(GlobalSystemMediaTransportControlsSessionManager sender, CurrentSessionChangedEventArgs args)
        {
            System.Console.WriteLine("INFO: session changed");
            // Unsubscribe from previous session's events
            if (currentSession != null)
            {
                currentSession.MediaPropertiesChanged -= Session_MediaPropertiesChanged;
                currentSession.PlaybackInfoChanged -= Session_PlaybackInfoChanged;
                currentSession.TimelinePropertiesChanged -= Session_TimelinePropertiesChanged;
            }

            // Update current session
            currentSession = sender.GetCurrentSession();
            if (currentSession != null)
            {
                // Subscribe to media properties changed event
                currentSession.MediaPropertiesChanged += Session_MediaPropertiesChanged;
                // Subscribe to playback info changed event
                currentSession.PlaybackInfoChanged += Session_PlaybackInfoChanged;
                // Subscribe to timeline properties changed event
                currentSession.TimelinePropertiesChanged += Session_TimelinePropertiesChanged;
                System.Console.WriteLine("Source: " + currentSession.SourceAppUserModelId);
            }
            else
            {
                Console.WriteLine("No media session is currently active.");
            }
        }

        private static async Task<(string, string, string, long, long, bool, IRandomAccessStreamReference)> GetCurrentSongInfo()
        {
            if (currentSession == null)
            {
                return (null, null, null, 0, 0, false, null);
            }

            var mediaProperties = await currentSession.TryGetMediaPropertiesAsync();
            var timelineProperties = currentSession.GetTimelineProperties();
            var title = mediaProperties.Title;
            var artist = mediaProperties.Artist;
            var album = mediaProperties.AlbumTitle;
            var startTime = new DateTimeOffset(DateTimeOffset.Now.DateTime - timelineProperties.Position);
            var endTime = new DateTimeOffset(DateTime.Now + timelineProperties.EndTime - timelineProperties.Position);
            var isPaused = currentSession.GetPlaybackInfo() != null && currentSession.GetPlaybackInfo().PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Paused;
            var thumbnail = mediaProperties.Thumbnail;
            return (title, artist, album, startTime.ToUnixTimeSeconds(), endTime.ToUnixTimeMilliseconds(), isPaused, thumbnail);
        }

        private static async Task<bool> SongInfoChanged(bool initial, bool newSong)
        {
            var (title, artist, album, startTime, endTime, isPaused, thumbnail) = await GetCurrentSongInfo();
            startTime = newSong ? DateTimeOffset.Now.ToUnixTimeSeconds() : startTime;
            if (isPaused)
            {
                if (!initial)
                {
                    System.Console.WriteLine("Song was paused. Clearing activity.");
                    ClearActivity();
                }

                return false;
            }

            if (title != null)
            {
                var (activity, guid) = YTM_Activity.GetYoutubeMusicActivity(title, artist, startTime, endTime);
                await SaveCurrentAlbumImage(thumbnail, guid);
                UpdateActivity(activity);
                return true;
            }

            return false;
        }

        private static async Task SaveCurrentAlbumImage(IRandomAccessStreamReference thumbnail, Guid guid)
        {
            if (thumbnail == null)
            {
                return;
            }
            using IRandomAccessStreamWithContentType streamWithContentType = await thumbnail.OpenReadAsync();
            lock (_albumCoverLock)
            {
                AlbumCoverServer.SetAlbumCoverGuid(guid);
                using var fileStream = new FileStream("./album_image/current_album.jpg", FileMode.Create, FileAccess.Write);
                using var inputStream = streamWithContentType.AsStreamForRead();
                inputStream.CopyTo(fileStream);
            }
        }
    }
}