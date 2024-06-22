using System;
using System.Threading;
using System.Threading.Tasks;
using Windows.Media.Control;

namespace MediaInfo
{
    class YTM_Activity
    {
        static public Discord.Activity GetYoutubeMusicActivity(Discord.ActivityManager activityManager, string songTitle, string artistName, TimeSpan timeLeft)
        {
            Random random = new();
            string imageUrl = $"https://ytmrpalbumcoverserver.teop.me/album_image/current_album.jpg?{random.Next()}";
            return new Discord.Activity
            {
                Type = Discord.ActivityType.Listening,
                Details = songTitle,
                State = artistName,
                Instance = true,
                Timestamps =
                {
                    Start =  DateTimeOffset.Now.ToUnixTimeSeconds(),
                    End = DateTimeOffset.Now.Add(timeLeft).ToUnixTimeSeconds()
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