﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.
// Based on https://github.com/dotnet/aspnetcore/commit/2ceca7fb89a4021166b32f18612bc490d3146fe2

using System;
using System.Collections.Concurrent;
using System.Text;
using Microsoft.Extensions.Logging;
using Uno.Foundation;

namespace Uno.Extensions.Logging.WebAssembly
{
	internal class WebAssemblyConsoleLogger : ILogger<object>, ILogger
	{
		private static readonly string _loglevelPadding = ": ";
		private static readonly string _messagePadding;
		private static readonly string _newLineWithMessagePadding;
		private static readonly StringBuilder _logBuilder = new();

		private readonly string _name;

		static WebAssemblyConsoleLogger()
		{
			var logLevelString = GetLogLevelString(LogLevel.Information);
			_messagePadding = new string(' ', logLevelString.Length + _loglevelPadding.Length);
			_newLineWithMessagePadding = Environment.NewLine + _messagePadding;
		}

		public WebAssemblyConsoleLogger()
			: this(string.Empty)
		{
		}

		public WebAssemblyConsoleLogger(string name)
		{
			_name = name ?? throw new ArgumentNullException(nameof(name));
		}

		public IDisposable BeginScope<TState>(TState state)
		{
			return NoOpDisposable.Instance;
		}

		public bool IsEnabled(LogLevel logLevel)
		{
			return logLevel != LogLevel.None;
		}

		public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
		{
			if (!IsEnabled(logLevel))
			{
				return;
			}

			if (formatter == null)
			{
				throw new ArgumentNullException(nameof(formatter));
			}

			var message = formatter(state, exception);

			if (!string.IsNullOrEmpty(message) || exception != null)
			{
				WriteMessage(logLevel, _name, eventId.Id, message, exception);
			}
		}

		private void WriteMessage(LogLevel logLevel, string logName, int eventId, string message, Exception exception)
		{
			lock (_logBuilder)
			{
				try
				{
					CreateDefaultLogMessage(_logBuilder, logLevel, logName, eventId, message, exception);
					var formattedMessage = _logBuilder.ToString();

					void Invoke(string method, string message) => WebAssemblyRuntime.InvokeJS($"{method}(\"{WebAssemblyRuntime.EscapeJs(message)}\")");

					switch (logLevel)
					{
						case LogLevel.Trace:
						case LogLevel.Debug:
							// Although https://console.spec.whatwg.org/#loglevel-severity claims that
							// "console.debug" and "console.log" are synonyms, that doesn't match the
							// behavior of browsers in the real world. Chromium only displays "debug"
							// messages if you enable "Verbose" in the filter dropdown (which is off
							// by default). As such "console.debug" is the best choice for messages
							// with a lower severity level than "Information".
							Invoke("console.debug", formattedMessage);
							break;
						case LogLevel.Information:
							Invoke("console.info", formattedMessage);
							break;
						case LogLevel.Warning:
							Invoke("console.warn", formattedMessage);
							break;
						case LogLevel.Error:
							Invoke("console.error", formattedMessage);
							break;
						case LogLevel.Critical:
							// Writing to Console.Error is even more severe than calling console.error,
							// because it also causes the error UI (gold bar) to appear.
							Console.Error.WriteLine(formattedMessage);
							break;
						default: // LogLevel.None or invalid enum values
							Console.WriteLine(formattedMessage);
							break;
					}
				}
				finally
				{
					_logBuilder.Clear();
				}
			}
		}

		private void CreateDefaultLogMessage(StringBuilder logBuilder, LogLevel logLevel, string logName, int eventId, string message, Exception exception)
		{
			logBuilder.Append(GetLogLevelString(logLevel));
			logBuilder.Append(_loglevelPadding);
			logBuilder.Append(logName);
			logBuilder.Append("[");
			logBuilder.Append(eventId);
			logBuilder.Append("]");

			if (!string.IsNullOrEmpty(message))
			{
				// message
				logBuilder.AppendLine();
				logBuilder.Append(_messagePadding);

				var len = logBuilder.Length;
				logBuilder.Append(message);
				logBuilder.Replace(Environment.NewLine, _newLineWithMessagePadding, len, message.Length);
			}

			// Example:
			// System.InvalidOperationException
			//    at Namespace.Class.Function() in File:line X
			if (exception != null)
			{
				// exception message
				logBuilder.AppendLine();
				logBuilder.Append(exception.ToString());
			}
		}

		private static string GetLogLevelString(LogLevel logLevel)
		{
			switch (logLevel)
			{
				case LogLevel.Trace:
					return "trce";
				case LogLevel.Debug:
					return "dbug";
				case LogLevel.Information:
					return "info";
				case LogLevel.Warning:
					return "warn";
				case LogLevel.Error:
					return "fail";
				case LogLevel.Critical:
					return "crit";
				default:
					throw new ArgumentOutOfRangeException(nameof(logLevel));
			}
		}

		private class NoOpDisposable : IDisposable
		{
			public static NoOpDisposable Instance = new();

			public void Dispose() { }
		}
	}
}
