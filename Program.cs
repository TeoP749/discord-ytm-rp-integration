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
                currentSession.TimelinePropertiesChanged += Sessoin_TimelinePropertiesChanged;

                await SongInfoChanged();
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
                discord.Dispose();
            }
        }

        private static void UpdateActivity(string songTitle, string artistName, TimeSpan timeLeft)
        {
            var activity = YTM_Activity.GetYoutubeMusicActivity(activityManager, songTitle, artistName, timeLeft);
            {
                activityManager.UpdateActivity(activity, result =>
                {
                    if (result == Discord.Result.Ok)
                    {
                        Console.WriteLine("Activity updated successfully.");
                    }
                    else
                    {
                        Console.WriteLine("Activity failed to update.");
                    }
                });
            }
        }

        private static void ClearActivity()
        {
            activityManager.ClearActivity(result =>
            {
                if (result == Discord.Result.Ok)
                {
                    Console.WriteLine("Activity cleared successfully.");
                }
                else
                {
                    Console.WriteLine("Activity failed to clear.");
                }
            });
        }

        private static async void Session_MediaPropertiesChanged(GlobalSystemMediaTransportControlsSession sender, MediaPropertiesChangedEventArgs args)
        {
            System.Console.WriteLine("Media properties changed.");
            await SongInfoChanged();
        }

        private static async void Sessoin_TimelinePropertiesChanged(GlobalSystemMediaTransportControlsSession sender, TimelinePropertiesChangedEventArgs args)
        {
            System.Console.WriteLine("Timeline properties changed.");
            await SongInfoChanged();
        }

        private static void SessionManager_CurrentSessionChanged(GlobalSystemMediaTransportControlsSessionManager sender, CurrentSessionChangedEventArgs args)
        {
            System.Console.WriteLine("Current session changed.");
            // Unsubscribe from previous session's events
            if (currentSession != null)
            {
                currentSession.MediaPropertiesChanged -= Session_MediaPropertiesChanged;
                currentSession.TimelinePropertiesChanged -= Sessoin_TimelinePropertiesChanged;
            }

            // Update current session
            currentSession = sender.GetCurrentSession();
            if (currentSession != null)
            {
                // Subscribe to media properties changed event
                currentSession.MediaPropertiesChanged += Session_MediaPropertiesChanged;
                // Subscribe to timeline properties changed event
                currentSession.TimelinePropertiesChanged += Sessoin_TimelinePropertiesChanged;
            }
            else
            {
                Console.WriteLine("No media session is currently active.");
            }
        }

        private static async Task<(string, string, TimeSpan, bool)> GetCurrentSongInfo()
        {
            if (currentSession == null)
            {
                return (null, null, TimeSpan.Zero, false);
            }

            var mediaProperties = await currentSession.TryGetMediaPropertiesAsync();
            var timelineProperties = currentSession.GetTimelineProperties();
            var title = mediaProperties.Title;
            var artist = mediaProperties.Artist;
            var timeLeft = timelineProperties.EndTime - timelineProperties.Position;
            var isPaused = currentSession.GetPlaybackInfo().PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Paused;
            await SaveCurrentAlbumImage(mediaProperties.Thumbnail);
            return (title, artist, timeLeft, isPaused);
        }

        private static async Task<bool> SongInfoChanged()
        {
            (string title, string artist, TimeSpan timeLeft, bool isPaused) = await GetCurrentSongInfo();

            // if (isPaused)
            // {
            //     System.Console.WriteLine("Song is paused.");
            //     ClearActivity();
            //     return false;
            // }

            if (title != null)
            {
                UpdateActivity(title, artist, timeLeft);
                return true;
            }

            return false;
        }

        private static async Task SaveCurrentAlbumImage(IRandomAccessStreamReference thumbnail)
        {
            using IRandomAccessStreamWithContentType streamWithContentType = await thumbnail.OpenReadAsync();
            lock (_albumCoverLock)
            {
                using var fileStream = new FileStream("./album_image/current_album.jpg", FileMode.Create, FileAccess.Write);
                using var inputStream = streamWithContentType.AsStreamForRead();
                inputStream.CopyTo(fileStream);
            }
        }
    }
}