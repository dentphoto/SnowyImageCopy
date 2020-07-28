﻿using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

using SnowyImageCopy.Models;
using SnowyImageCopy.Views;

namespace SnowyImageCopy
{
	public partial class App : Application
	{
		public App()
		{
			if (!Debugger.IsAttached)
				AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
		}

		protected override void OnStartup(StartupEventArgs e)
		{
			base.OnStartup(e);

			if (!Debugger.IsAttached)
				this.DispatcherUnhandledException += OnDispatcherUnhandledException;

			if (CommandLine.ShowsUsage)
			{
				CommandLine.ShowUsage();
				this.Shutdown(0); // This shutdown is expected behavior.
				return;
			}

			if (!TryCreateSemaphore(SnowyImageCopy.Properties.Settings.Default.AppId))
			{
				this.Shutdown(0);
				return;
			}

			Settings.Current.Start();
			ResourceService.Current.ChangeCulture(Settings.Current.CultureName);

			this.MainWindow = new MainWindow();
			this.MainWindow.Show();
		}

		protected override void OnExit(ExitEventArgs e)
		{
			Settings.Current.Stop();
			CloseSemaphore();

			base.OnExit(e);
		}

		#region Semaphore

		private Semaphore _semaphore;

		private bool TryCreateSemaphore(string name)
		{
			if (string.IsNullOrEmpty(name))
				throw new ArgumentNullException(nameof(name));

			_semaphore = new Semaphore(1, 1, name, out bool createdNew);
			return createdNew;
		}

		private void CloseSemaphore() => _semaphore?.Dispose();

		#endregion

		#region Exception

		private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
		{
			LogService.RecordException(sender, e.ExceptionObject as Exception);
		}

		private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
		{
			LogService.RecordException(sender, e.Exception);

			e.Handled = true;
			this.Shutdown(1); // This shutdown is for unusual case.
		}

		#endregion
	}
}