// TSLib - A free TeamSpeak 3 and 5 client library
// Copyright (C) 2017  TSLib contributors
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the Open Software License v. 3.0
//
// You should have received a copy of the Open Software License along with this
// program. If not, see <https://opensource.org/licenses/OSL-3.0>.

using System;
using System.Threading;
using System.Threading.Tasks;
using TSLib.Messages;

namespace TSLib
{
	internal abstract class WaitBlock : IDisposable
	{
		protected static readonly NLog.Logger Log = NLog.LogManager.GetCurrentClassLogger();
		protected readonly ManualResetEvent? notificationWaiter;
		protected readonly Deserializer deserializer;
		protected R<ReadOnlyMemory<byte>, CommandError> result;
		public NotificationType[]? DependsOn { get; }
		protected LazyNotification notification;
		protected bool isDisposed;
		protected static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(15);

		public WaitBlock(Deserializer deserializer, NotificationType[]? dependsOn = null)
		{
			this.deserializer = deserializer;
			isDisposed = false;
			DependsOn = dependsOn;
			if (DependsOn != null)
			{
				if (DependsOn.Length == 0)
					throw new InvalidOperationException("Depending notification array must not be empty");
				notificationWaiter = new ManualResetEvent(false);
			}
		}

		public void SetError(CommandError commandError)
		{
			if (commandError.Id == TsErrorCode.ok)
				throw new ArgumentException("Passed explicit error without error code", nameof(commandError));
			SetAnswerAuto(commandError, null);
		}

		public void SetAnswer(ReadOnlyMemory<byte> commandLine) => SetAnswerAuto(null, commandLine);

		public void SetAnswerAuto(CommandError? commandError, ReadOnlyMemory<byte>? commandLine)
		{
			if (isDisposed)
				return;
			if (commandError != null && commandError.Id != TsErrorCode.ok)
			{
				result = commandError;
			}
			else if (commandLine != null)
			{
				result = commandLine.Value;
			}
			else
			{
				Log.Debug("Maybe missing line data:{@data} line:null", commandLine);
				result = ReadOnlyMemory<byte>.Empty;
			}
			Trigger();
		}

		protected abstract void Trigger();

		public void SetNotification(LazyNotification notification)
		{
			if (isDisposed)
				return;
			if (DependsOn != null && Array.IndexOf(DependsOn, notification.NotifyType) < 0)
				throw new ArgumentException("The notification does not match this waitblock");
			this.notification = notification;
			notificationWaiter!.Set();
		}

		public virtual void Dispose()
		{
			if (isDisposed)
				return;
			isDisposed = true;

			if (notificationWaiter != null)
			{
				notificationWaiter.Set();
				notificationWaiter.Dispose();
			}
		}
	}

	internal sealed class WaitBlockSync : WaitBlock
	{
		private readonly ManualResetEvent answerWaiter;

		public WaitBlockSync(Deserializer deserializer, NotificationType[]? dependsOn = null) : base(deserializer, dependsOn)
		{
			answerWaiter = new ManualResetEvent(false);
		}

		public R<T[], CommandError> WaitForMessage<T>() where T : IResponse, new()
		{
			if (isDisposed)
				throw new ObjectDisposedException(nameof(WaitBlock));
			if (!answerWaiter.WaitOne(CommandTimeout))
				return CommandError.TimeOut;
			if (!result.Ok)
				return result.Error;

			var response = deserializer.GenerateResponse<T>(this.result.Value.Span);
			if (response is null)
				return CommandError.Parser;
			else
				return response;
		}

		public R<LazyNotification, CommandError> WaitForNotification()
		{
			if (isDisposed)
				throw new ObjectDisposedException(nameof(WaitBlock));
			if (DependsOn is null)
				throw new InvalidOperationException("This waitblock has no dependent Notification");
			if (!answerWaiter.WaitOne(CommandTimeout))
				return CommandError.TimeOut;
			if (!result.Ok)
				return result.Error;
			if (!notificationWaiter!.WaitOne(CommandTimeout))
				return CommandError.TimeOut;

			return notification;
		}

		protected override void Trigger() => answerWaiter.Set();

		public override void Dispose()
		{
			if (isDisposed)
				return;

			base.Dispose();

			answerWaiter.Set();
			answerWaiter.Dispose();
		}
	}

	internal sealed class WaitBlockAsync : WaitBlock
	{
		private readonly TaskCompletionSource<bool> answerWaiterAsync;

		public WaitBlockAsync(Deserializer deserializer, NotificationType[]? dependsOn = null) : base(deserializer, dependsOn)
		{
			answerWaiterAsync = new TaskCompletionSource<bool>();
		}

		public async Task<R<T[], CommandError>> WaitForMessageAsync<T>() where T : IResponse, new()
		{
			if (isDisposed)
				throw new ObjectDisposedException(nameof(WaitBlock));
			var timeOut = Task.Delay(CommandTimeout);
			var res = await Task.WhenAny(answerWaiterAsync.Task, timeOut).ConfigureAwait(false);
			if (res == timeOut)
				return CommandError.TimeOut;
			if (!result.Ok)
				return result.Error;

			var response = deserializer.GenerateResponse<T>(result.Value.Span);
			if (response is null)
				return CommandError.Parser;
			else
				return response;
		}

		public async Task<R<LazyNotification, CommandError>> WaitForNotificationAsync() // TODO improve non-blocking
		{
			if (isDisposed)
				throw new ObjectDisposedException(nameof(WaitBlock));
			if (DependsOn is null)
				throw new InvalidOperationException("This waitblock has no dependent Notification");
			var timeOut = Task.Delay(CommandTimeout);
			var res = await Task.WhenAny(answerWaiterAsync.Task, timeOut).ConfigureAwait(false);
			if (res == timeOut)
				return CommandError.TimeOut;
			if (!result.Ok)
				return result.Error;
			if (!notificationWaiter!.WaitOne(CommandTimeout))
				return CommandError.TimeOut;

			return notification;
		}

		protected override void Trigger() => answerWaiterAsync.SetResult(true);

		public override void Dispose()
		{
			if (isDisposed)
				return;

			base.Dispose();
		}
	}
}
