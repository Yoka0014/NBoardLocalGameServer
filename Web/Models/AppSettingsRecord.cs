namespace NBoardLocalGameServer.Web.Models
{
    internal class AppSettingsRecord
    {
        public bool AutoStopWhenQueueEmpty { get; set; }

        /// <summary>How many consecutive minutes the queue must stay empty before self-stopping the EC2 instance.</summary>
        public int AutoStopIdleMinutes { get; set; } = 15;

        /// <summary>
        /// Shell command run after every completed match to push data/history-export/ to a static host
        /// (e.g. an rclone/aws-cli invocation). The literal token "{dir}" is replaced with the export
        /// folder's absolute path. Left empty, no sync happens (the export folder is still populated
        /// locally, just never pushed anywhere).
        /// </summary>
        public string? HistorySyncCommand { get; set; }
    }
}
