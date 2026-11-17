using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WeatherHueWallpaper
{
	public class Logger
	{
		public void Start()
		{
			var thread = new Thread(LoggerProc);

			thread.IsBackground = true;
			thread.Name = "Logger";
			thread.Start();
		}

		public void Log(string format, params object[] args)
			=> Log(string.Format(format, args));

		public void Log(string message)
		{
			lock (_sync)
			{
				if (!message.Contains('\n'))
				{
					var entry = new LogEntry() { Message = message };

					_logEntryQueue.Enqueue(entry);
				}
				else
				{
					var timeStamp = DateTime.Now;

					var reader = new StringReader(message);

					while (reader.ReadLine() is string line)
					{
						var entry = new LogEntry() { TimeStamp = timeStamp, Message = line };

						_logEntryQueue.Enqueue(entry);
					}
				}
			}
		}

		object _sync = new object();
		Queue<LogEntry> _logEntryQueue = new Queue<LogEntry>();

		void LoggerProc()
		{
			while (true)
			{
				Thread.Sleep(TimeSpan.FromSeconds(10));

				lock (_sync)
				{
					if (_logEntryQueue.Count > 0)
					{
						using (var writer = new StreamWriter(GetLogFilePath(), append: true))
						{
							while (_logEntryQueue.TryDequeue(out var entry))
								writer.WriteLine("[{0:yyyy-MM-dd HH:mm:ss.fff}] {1}", entry.TimeStamp, entry.Message);
						}
					}
				}
			}
		}

		static string GetLogFilePath()
		{
			var today = DateTime.Now;

			string path = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

			return Path.Combine(path, $"WeatherHueWallpaper_{today:yyyy-MM-dd}.log");
		}
	}

	public class LogEntry
	{
		public DateTime TimeStamp = DateTime.Now;
		public string Message = "";
	}
}
