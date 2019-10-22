// TS3AudioBot - An advanced Musicbot for Teamspeak 3
// Copyright (C) 2017  TS3AudioBot contributors
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the Open Software License v. 3.0
//
// You should have received a copy of the Open Software License along with this
// program. If not, see <https://opensource.org/licenses/OSL-3.0>.

namespace TS3AudioBot.CommandSystem.Commands
{
	using CommandResults;
	using Localization;
	using System;
	using System.Collections.Generic;
	using System.Globalization;
	using System.Linq;

	/// <summary>
	/// A command that stores a result and returns it.
	/// </summary>
	public class TailStringAutoConvertCommand : ICommand
	{
		private static readonly NLog.Logger Log = NLog.LogManager.GetCurrentClassLogger();

		public TailString Content { get; }

		public TailStringAutoConvertCommand(TailString contentArg)
		{
			Content = contentArg;
		}

		public virtual object Execute(ExecutionInformation info, IReadOnlyList<ICommand> arguments, IReadOnlyList<Type> returnTypes)
		{
			foreach (var type in returnTypes)
			{
				if (type == typeof(TailString))
					return Content;

				try
				{
					var result = Convert.ChangeType(Content.Content, type, CultureInfo.InvariantCulture);
					Log.Debug($"Converting command result {Content} to {type} returns {result}");

					return ResultHelper.ToResult(type, result);
				}
				catch
				{
					Log.Debug($"Converting command result {Content} to {type} failed");
				}
			}
			throw new CommandException(strings.error_cmd_no_matching_overload, CommandExceptionReason.NoReturnMatch);
		}

		public override string ToString() => "<auto-convert-result>";
	}
}
