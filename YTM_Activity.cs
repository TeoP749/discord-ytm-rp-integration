using System;
using System.Threading;
using System.Threading.Tasks;
using Windows.Media.Control;

namespace MediaInfo
{
    class YTM_Activity
    {
        static public Discord.Activity GetYoutubeMusicActivity(string songTitle, string artistName, TimeSpan position, TimeSpan duration)
        {
            Random random = new();
            string imageUrl = $"https://ytmrpalbumcoverserver.teop.me/album_image/current_album.jpg?{random.Next()}";
            return new Discord.Activity
            {
                Type = Discord.ActivityType.Listening,
                Details = songTitle,
                State = artistName,
                Instance = true,
                SupportedPlatforms = 1,
                Timestamps =
                {
                    Start =  DateTimeOffset.Now.ToUnixTimeSeconds() - (long)position.TotalSeconds,
                    End = DateTimeOffset.Now.Add(duration - position).ToUnixTimeSeconds()
                },
                Assets =
                {
                    LargeImage = imageUrl,
                    LargeText = "Album Cover"
                }
            };
        }
    }
}