﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;

using SnowyImageCopy.Helper;
using SnowyImageCopy.Models.Exceptions;

namespace SnowyImageCopy.Models
{
	/// <summary>
	/// Manages FlashAir card.
	/// </summary>
	internal class FileManager : IDisposable
	{
		#region Constant

		/// <summary>
		/// Interval to monitor network connection during operation
		/// </summary>
		private static readonly TimeSpan _monitorInterval = TimeSpan.FromSeconds(2);

		/// <summary>
		/// Interval of retry
		/// </summary>
		private static readonly TimeSpan _retryInterval = TimeSpan.FromMilliseconds(500);

		/// <summary>
		/// The maximum count of retry
		/// </summary>
		private const int MaxRetryCount = 3;

		#endregion

		#region Instance member

		private HttpClient Client
		{
			get
			{
				if ((_client is null) || (_remoteRoot != Settings.Current.RemoteRoot))
				{
					_client?.Dispose();

					_client = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
					_remoteRoot = Settings.Current.RemoteRoot;
				}
				return _client;
			}
		}
		private HttpClient _client;
		private string _remoteRoot;

		#region IDisposable member

		private bool _disposed = false;

		public void Dispose()
		{
			Dispose(true);
			GC.SuppressFinalize(this);
		}

		protected virtual void Dispose(bool disposing)
		{
			if (_disposed)
				return;

			if (disposing)
			{
				_client?.Dispose();
			}

			_disposed = true;
		}

		#endregion

		internal Task<IEnumerable<IFileItem>> GetFileListRootAsync(CardInfo card, CancellationToken cancellationToken) =>
			GetFileListRootAsync(Client, card, cancellationToken);

		internal Task<IEnumerable<IFileItem>> GetFileListAsync(string remoteDirectoryPath, CardInfo card, CancellationToken cancellationToken) =>
			GetFileListAsync(Client, remoteDirectoryPath, card, cancellationToken);

		internal Task<int> GetFileNumAsync(string remoteDirectoryPath, CardInfo card, CancellationToken cancellationToken) =>
			GetFileNumAsync(Client, remoteDirectoryPath, card, cancellationToken);

		internal Task<BitmapSource> GetThumbnailAsync(string remoteFilePath, CardInfo card, CancellationToken cancellationToken) =>
			GetThumbnailAsync(Client, remoteFilePath, card, cancellationToken);

		internal Task<byte[]> GetSaveFileAsync(string remoteFilePath, string localFilePath, int size, DateTime itemDate, bool canReadExif, IProgress<ProgressInfo> progress, CardInfo card, CancellationToken cancellationToken) =>
			GetSaveFileAsync(Client, remoteFilePath, localFilePath, size, itemDate, canReadExif, progress, card, cancellationToken);

		internal Task DeleteFileAsync(string remoteFilePath, CancellationToken cancellationToken) =>
			DeleteFileAsync(Client, remoteFilePath, cancellationToken);

		internal Task<string> GetFirmwareVersionAsync(CancellationToken cancellationToken) =>
			GetFirmwareVersionAsync(Client, cancellationToken);

		internal Task<string> GetCidAsync(CancellationToken cancellationToken) =>
			GetCidAsync(Client, cancellationToken);

		internal Task<string> GetSsidAsync(CancellationToken cancellationToken) =>
			GetSsidAsync(Client, cancellationToken);

		internal Task<bool> CheckUpdateStatusAsync() =>
			CheckUpdateStatusAsync(Client);

		internal Task<int> GetWriteTimeStampAsync(CancellationToken token) =>
			GetWriteTimeStampAsync(Client, token);

		internal Task<string> GetUploadAsync(CancellationToken cancellationToken) =>
			GetUploadAsync(Client, cancellationToken);

		#endregion

		#region Static member (Internal)

		/// <summary>
		/// Gets a list of all files recursively from root folder of FlashAir card.
		/// </summary>
		/// <param name="client">HttpClient</param>
		/// <param name="card">FlashAir card information</param>
		/// <param name="cancellationToken">CancellationToken</param>
		/// <returns>File list</returns>
		internal static async Task<IEnumerable<IFileItem>> GetFileListRootAsync(HttpClient client, CardInfo card, CancellationToken cancellationToken)
		{
			try
			{
				return await GetFileListAllAsync(client, Settings.Current.RemoteDescendant, card, cancellationToken).ConfigureAwait(false);
			}
			catch
			{
				Debug.WriteLine("Failed to get all file list.");
				throw;
			}
		}

		/// <summary>
		/// Gets a list of all files recursively in a specified directory in FlashAir card.
		/// </summary>
		/// <param name="client">HttpClient</param>
		/// <param name="remoteDirectoryPath">Remote directory path</param>
		/// <param name="card">FlashAir card information</param>
		/// <param name="cancellationToken">CancellationToken</param>
		/// <returns>File list</returns>
		/// <remarks>This method is part of parent method.</remarks>
		private static async Task<List<IFileItem>> GetFileListAllAsync(HttpClient client, string remoteDirectoryPath, CardInfo card, CancellationToken cancellationToken)
		{
			var itemList = await GetFileListEachAsync(client, remoteDirectoryPath, card, cancellationToken).ConfigureAwait(false);

			for (int i = itemList.Count - 1; 0 <= i; i--)
			{
				var item = itemList[i];

				if (item.IsHidden || item.IsSystem || item.IsVolume ||
					item.IsFlashAirSystem)
				{
					itemList.RemoveAt(i);
					continue;
				}

				if (!item.IsDirectory)
				{
					if (!item.IsImageFile)
					{
						itemList.RemoveAt(i);
					}
					continue;
				}

				var path = item.FilePath;
				itemList.RemoveAt(i);
				itemList.AddRange(await GetFileListAllAsync(client, path, card, cancellationToken).ConfigureAwait(false));
			}
			return itemList;
		}

		/// <summary>
		/// Gets a list of files in a specified directory in FlashAir card.
		/// </summary>
		/// <param name="client">HttpClient</param>
		/// <param name="remoteDirectoryPath">Remote directory path</param>
		/// <param name="card">FlashAir card information</param>
		/// <param name="cancellationToken">CancellationToken</param>
		/// <returns>File list</returns>
		/// <remarks>This method is part of parent method.</remarks>
		private static async Task<List<IFileItem>> GetFileListEachAsync(HttpClient client, string remoteDirectoryPath, CardInfo card, CancellationToken cancellationToken)
		{
			var remotePath = ComposeRemotePath(FileManagerCommand.GetFileList, remoteDirectoryPath);

			var fileEntries = await DownloadStringAsync(client, remotePath, card, cancellationToken).ConfigureAwait(false);

			return fileEntries.Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries)
				.Select<string, IFileItem>(fileEntry => new FileItem(fileEntry, remoteDirectoryPath))
				.Where(x => x.IsImported)
				.ToList();
		}

		/// <summary>
		/// Gets a list of files in a specified directory in FlashAir card.
		/// </summary>
		/// <param name="client">HttpClient</param>
		/// <param name="remoteDirectoryPath">Remote directory path</param>
		/// <param name="card">FlashAir card information</param>
		/// <param name="cancellationToken">CancellationToken</param>
		/// <returns>File list</returns>
		/// <remarks>This method is not actually used.</remarks>
		internal static async Task<IEnumerable<IFileItem>> GetFileListAsync(HttpClient client, string remoteDirectoryPath, CardInfo card, CancellationToken cancellationToken)
		{
			if (string.IsNullOrWhiteSpace(remoteDirectoryPath))
				throw new ArgumentNullException(nameof(remoteDirectoryPath));

			var remotePath = ComposeRemotePath(FileManagerCommand.GetFileList, remoteDirectoryPath);

			try
			{
				var fileEntries = await DownloadStringAsync(client, remotePath, card, cancellationToken).ConfigureAwait(false);

				return fileEntries.Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries)
					.Select<string, IFileItem>(fileEntry => new FileItem(fileEntry, remoteDirectoryPath))
					.Where(x => x.IsImported)
					.ToList();
			}
			catch
			{
				Debug.WriteLine("Failed to get file list.");
				throw;
			}
		}

		/// <summary>
		/// Gets the number of files in a specified directory in FlashAir card.
		/// </summary>
		/// <param name="client">HttpClient</param>
		/// <param name="remoteDirectoryPath">Remote directory path</param>
		/// <param name="card">FlashAir card information</param>
		/// <param name="cancellationToken">CancellationToken</param>
		/// <returns>The number of files</returns>
		/// <remarks>This method is not actually used.</remarks>
		internal static async Task<int> GetFileNumAsync(HttpClient client, string remoteDirectoryPath, CardInfo card, CancellationToken cancellationToken)
		{
			if (string.IsNullOrWhiteSpace(remoteDirectoryPath))
				throw new ArgumentNullException(nameof(remoteDirectoryPath));

			var remotePath = ComposeRemotePath(FileManagerCommand.GetFileNum, remoteDirectoryPath);

			try
			{
				var fileNum = await DownloadStringAsync(client, remotePath, card, cancellationToken).ConfigureAwait(false);

				return int.TryParse(fileNum, out var num) ? num : 0;
			}
			catch
			{
				Debug.WriteLine("Failed to get the number of files.");
				throw;
			}
		}

		/// <summary>
		/// Gets a thumbnail of a specified image file in FlashAir card.
		/// </summary>
		/// <param name="client">HttpClient</param>
		/// <param name="remoteFilePath">Remote file path</param>
		/// <param name="card">FlashAir card information</param>
		/// <param name="cancellationToken">CancellationToken</param>
		/// <returns>Thumbnail of image file</returns>
		internal static async Task<BitmapSource> GetThumbnailAsync(HttpClient client, string remoteFilePath, CardInfo card, CancellationToken cancellationToken)
		{
			if (string.IsNullOrWhiteSpace(remoteFilePath))
				throw new ArgumentNullException(nameof(remoteFilePath));

			var remotePath = ComposeRemotePath(FileManagerCommand.GetThumbnail, remoteFilePath);

			try
			{
				var bytes = await DownloadBytesAsync(client, remotePath, card, cancellationToken).ConfigureAwait(false);

				return await ImageManager.ConvertBytesToBitmapSourceAsync(bytes).ConfigureAwait(false);
			}
			catch (ImageNotSupportedException)
			{
				// This exception should not be thrown because thumbnail data is directly provided by FlashAir card.
				return null;
			}
			catch (RemoteFileNotFoundException)
			{
				// If the format of image file is not JPEG or if there is no Exif standardized thumbnail stored,
				// StatusCode will be HttpStatusCode.NotFound.
				throw new RemoteFileThumbnailFailedException("Image file is not JPEG format or does not contain standardized thumbnail.", remotePath);
			}
			catch (RemoteConnectionUnableException rcue) when (rcue.Code == HttpStatusCode.InternalServerError)
			{
				// If image file is non-standard JPEG format, StatusCode may be HttpStatusCode.InternalServerError.
				throw new RemoteFileThumbnailFailedException("Image file is non-standard JPEG format.", remotePath);
			}
			catch
			{
				Debug.WriteLine("Failed to get a thumbnail.");
				throw;
			}
		}

		/// <summary>
		/// Gets file data of a specified remote file in FlashAir card and save it in local folder.
		/// </summary>
		/// <param name="client">HttpClient</param>
		/// <param name="remoteFilePath">Remote file path</param>
		/// <param name="localFilePath">Local file path</param>
		/// <param name="size">File size provided by FlashAir card</param>
		/// <param name="itemDate">Date provided by FlashAir card</param>
		/// <param name="canReadExif">Whether can read Exif metadata from the file</param>
		/// <param name="progress">Progress</param>
		/// <param name="card">FlashAir card information</param>
		/// <param name="cancellationToken">CancellationToken</param>
		/// <returns>Byte array of file</returns>
		internal static async Task<byte[]> GetSaveFileAsync(HttpClient client, string remoteFilePath, string localFilePath, int size, DateTime itemDate, bool canReadExif, IProgress<ProgressInfo> progress, CardInfo card, CancellationToken cancellationToken)
		{
			if (string.IsNullOrWhiteSpace(remoteFilePath))
				throw new ArgumentNullException(nameof(remoteFilePath));

			if (string.IsNullOrWhiteSpace(localFilePath))
				throw new ArgumentNullException(nameof(localFilePath));

			var remotePath = ComposeRemotePath(FileManagerCommand.None, remoteFilePath);
			byte[] bytes;

			try
			{
				bytes = await DownloadBytesAsync(client, remotePath, size, progress, card, cancellationToken).ConfigureAwait(false);
			}
			catch
			{
				Debug.WriteLine("Failed to get a file.");
				throw;
			}

			int retryCount = 0;

			while (true)
			{
				try
				{
					using (var fs = new FileStream(localFilePath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite))
					{
						var creationTime = itemDate;
						var lastWriteTime = itemDate;

						// Overwrite creation time by date of image taken from Exif metadata.
						if (canReadExif)
						{
							var exifDateTaken = await ImageManager.GetExifDateTakenAsync(bytes, DateTimeKind.Local).ConfigureAwait(false);
							if (exifDateTaken != default)
								creationTime = exifDateTaken;
						}

						FileTime.SetFileTime(fs.SafeFileHandle, creationTime: creationTime, lastWriteTime: lastWriteTime);

						await fs.WriteAsync(bytes, 0, bytes.Length, cancellationToken).ConfigureAwait(false);
					}
					return bytes;
				}
				catch (IOException) when (++retryCount < MaxRetryCount)
				{
					// Wait interval before retry.
					if (TimeSpan.Zero < _retryInterval)
						await Task.Delay(_retryInterval, cancellationToken);
				}
				catch
				{
					Debug.WriteLine("Failed to save a file.");
					throw;
				}
			}
		}

		/// <summary>
		/// Deletes a specified remote file in FlashAir card.
		/// </summary>
		/// <param name="client">HttpClient</param>
		/// <param name="remoteFilePath">Remote file path</param>
		/// <param name="cancellationToken">CancellationToken</param>
		internal static async Task DeleteFileAsync(HttpClient client, string remoteFilePath, CancellationToken cancellationToken)
		{
			if (string.IsNullOrWhiteSpace(remoteFilePath))
				throw new ArgumentNullException(nameof(remoteFilePath));

			var remotePath = ComposeRemotePath(FileManagerCommand.DeleteFile, remoteFilePath);

			try
			{
				var result = await DownloadStringAsync(client, remotePath, null, cancellationToken).ConfigureAwait(false);

				// "SUCCESS": If succeeded.
				// "ERROR":   If failed.
				if (!result.Equals("SUCCESS", StringComparison.Ordinal))
					throw new RemoteFileDeletionFailedException(result, remotePath);
			}
			catch (RemoteFileNotFoundException)
			{
				// If upload.cgi is disabled, StatusCode will be HttpStatusCode.NotFound.
				throw new RemoteFileDeletionFailedException(null, remotePath);
			}
			catch
			{
				Debug.WriteLine("Failed to delete a remote file.");
				throw;
			}
		}

		/// <summary>
		/// Gets firmware version of FlashAir card.
		/// </summary>
		/// <param name="client">HttpClient</param>
		/// <param name="cancellationToken">CancellationToken</param>
		/// <remarks>Firmware version</remarks>
		internal static async Task<string> GetFirmwareVersionAsync(HttpClient client, CancellationToken cancellationToken)
		{
			var remotePath = ComposeRemotePath(FileManagerCommand.GetFirmwareVersion, string.Empty);

			try
			{
				return await DownloadStringAsync(client, remotePath, null, cancellationToken).ConfigureAwait(false);
			}
			catch
			{
				Debug.WriteLine("Failed to get firmware version.");
				throw;
			}
		}

		/// <summary>
		/// Gets CID of FlashAir card.
		/// </summary>
		/// <param name="client">HttpClient</param>
		/// <param name="cancellationToken">CancellationToken</param>
		/// <returns>If succeeded, CID. If failed, empty string.</returns>
		internal static async Task<string> GetCidAsync(HttpClient client, CancellationToken cancellationToken)
		{
			var remotePath = ComposeRemotePath(FileManagerCommand.GetCid, string.Empty);

			try
			{
				return await DownloadStringAsync(client, remotePath, null, cancellationToken).ConfigureAwait(false);
			}
			catch (RemoteConnectionUnableException)
			{
				return string.Empty;
			}
			catch
			{
				Debug.WriteLine("Failed to get CID.");
				throw;
			}
		}

		/// <summary>
		/// Gets SSID of FlashAir card.
		/// </summary>
		/// <param name="client">HttpClient</param>
		/// <param name="cancellationToken">CancellationToken</param>
		/// <returns>SSID</returns>
		internal static async Task<string> GetSsidAsync(HttpClient client, CancellationToken cancellationToken)
		{
			var remotePath = ComposeRemotePath(FileManagerCommand.GetSsid, string.Empty);

			try
			{
				return await DownloadStringAsync(client, remotePath, null, cancellationToken).ConfigureAwait(false);
			}
			catch
			{
				Debug.WriteLine("Failed to get SSID.");
				throw;
			}
		}

		/// <summary>
		/// Checks update status of FlashAir card.
		/// </summary>
		/// <param name="client">HttpClient</param>
		/// <returns>True if update status is set</returns>
		internal static async Task<bool> CheckUpdateStatusAsync(HttpClient client)
		{
			var remotePath = ComposeRemotePath(FileManagerCommand.GetUpdateStatus, string.Empty);

			try
			{
				var status = await DownloadStringAsync(client, remotePath, null, CancellationToken.None).ConfigureAwait(false);

				// 1: If memory has been updated.
				// 0: If not.
				return (status == "1");
			}
			catch
			{
				Debug.WriteLine("Failed to check update status.");
				throw;
			}
		}

		/// <summary>
		/// Gets time stamp of write event in FlashAir card.
		/// </summary>
		/// <param name="client">HttpClient</param>
		/// <param name="cancellationToken">CancellationToken</param>
		/// <returns>If succeeded, time stamp (msec). If failed, -1.</returns>
		/// <remarks>If no write event occurred since FlashAir card started running, this value will be 0.</remarks>
		internal static async Task<int> GetWriteTimeStampAsync(HttpClient client, CancellationToken cancellationToken)
		{
			var remotePath = ComposeRemotePath(FileManagerCommand.GetWriteTimeStamp, string.Empty);

			try
			{
				var timeStamp = await DownloadStringAsync(client, remotePath, null, cancellationToken).ConfigureAwait(false);

				return int.TryParse(timeStamp, out var num) ? num : -1;
			}
			catch (RemoteConnectionUnableException)
			{
				// If request for time stamp of write event is not supported, StatusCode will be HttpStatusCode.BadRequest.
				return -1;
			}
			catch
			{
				Debug.WriteLine("Failed to get time stamp of write event.");
				throw;
			}
		}

		/// <summary>
		/// Gets Upload parameters of FlashAir card.
		/// </summary>
		/// <param name="client">HttpClient</param>
		/// <param name="cancellationToken">CancellationToken</param>
		/// <returns>If succeeded, Upload parameters (string). If failed, empty string.</returns>
		internal static async Task<string> GetUploadAsync(HttpClient client, CancellationToken cancellationToken)
		{
			var remotePath = ComposeRemotePath(FileManagerCommand.GetUpload, string.Empty);

			try
			{
				return await DownloadStringAsync(client, remotePath, null, cancellationToken).ConfigureAwait(false);
			}
			catch (RemoteConnectionUnableException)
			{
				// If request for Upload parameters is not supported, StatusCode will be HttpStatusCode.BadRequest.
				return string.Empty;
			}
			catch
			{
				Debug.WriteLine("Failed to get Upload parameters.");
				throw;
			}
		}

		#endregion

		#region Static Method (Private)

		private static async Task<string> DownloadStringAsync(HttpClient client, string path, CardInfo card, CancellationToken cancellationToken)
		{
			var bytes = await DownloadBytesAsync(client, path, 0, null, card, cancellationToken).ConfigureAwait(false);

			if (_recordsDownloadString)
				await RecordDownloadStringAsync(path, bytes).ConfigureAwait(false);

			// Response from FlashAir card seems to be encoded by ASCII.
			return Encoding.ASCII.GetString(bytes);
		}

		private static Task<byte[]> DownloadBytesAsync(HttpClient client, string path, CardInfo card, CancellationToken cancellationToken)
		{
			return DownloadBytesAsync(client, path, 0, null, card, cancellationToken);
		}

		private static Task<byte[]> DownloadBytesAsync(HttpClient client, string path, int size, CardInfo card, CancellationToken cancellationToken)
		{
			return DownloadBytesAsync(client, path, size, null, card, cancellationToken);
		}

		private static async Task<byte[]> DownloadBytesAsync(HttpClient client, string path, int size, IProgress<ProgressInfo> progress, CardInfo card, CancellationToken cancellationToken)
		{
			var timeoutDuration = TimeSpan.FromSeconds(Settings.Current.TimeoutDuration);
			int retryCount = 0;

			while (true)
			{
				try
				{
					try
					{
						using (var response = await client.GetAsync(path, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false))
						{
							// If HttpResponseMessage.EnsureSuccessStatusCode is set, an exception by this setting
							// will be thrown in the scope of HttpClient and so cannot be caught in this method.
							switch (response.StatusCode)
							{
								case HttpStatusCode.OK:
									// None.
									break;
								case HttpStatusCode.Unauthorized:
								case HttpStatusCode.InternalServerError:
								case HttpStatusCode.BadRequest:
								case HttpStatusCode.ServiceUnavailable:
									throw new RemoteConnectionUnableException(response.StatusCode);
								case HttpStatusCode.NotFound:
									// This status code does not always mean that the specified file is missing.
									throw new RemoteFileNotFoundException("File is missing or request cannot be handled!", path);
								default:
									throw new HttpRequestException($"StatusCode: {response.StatusCode}");
							}

							if ((0 < size) &&
								(response.Content.Headers.ContentLength != size))
								throw new RemoteFileInvalidException("Data length does not match!", path);

							// Because of HttpCompletionOption.ResponseHeadersRead option, neither CancellationToken
							// nor HttpClient.Timeout setting works for response content.

							// Register delegate to CancellationToken because CancellationToken can no longer
							// directly affect HttpClient. Disposing the HttpResponseMessage will make ReadAsStreamAsync
							// method throw an ObjectDisposedException and so exit this operation.
							var ctr = new CancellationTokenRegistration();
							try
							{
								ctr = cancellationToken.Register(() => response.Dispose());
							}
							catch (ObjectDisposedException ode)
							{
								// If CancellationTokenSource has been disposed during operation (it unlikely happens),
								// this exception will be thrown.
								Debug.WriteLine($"CancellationTokenSource has been disposed when tried to register delegate.\r\n{ode}");
							}
							using (ctr)
							{
								var tcs = new TaskCompletionSource<bool>();

								// Start timer to monitor network connection.
								using (var monitorTimer = new Timer(s =>
								{
									if (!NetworkChecker.IsNetworkConnected(card))
									{
										((TaskCompletionSource<bool>)s).TrySetResult(true);
									}
								}, tcs, _monitorInterval, _monitorInterval))
								{
									var monitorTask = tcs.Task;

									if ((size == 0) || (progress is null))
									{
										// Route without progress reporting
										var readTask = Task.Run(async () => await response.Content.ReadAsByteArrayAsync());
										var timeoutTask = Task.Delay(timeoutDuration);

										var completedTask = await Task.WhenAny(readTask, timeoutTask, monitorTask);
										if (completedTask == timeoutTask)
											throw new TimeoutException("Reading response content timed out!");
										if (completedTask == monitorTask)
											throw new RemoteConnectionLostException("Connection lost!");

										var bytes = await readTask;

										if ((0 < size) && (bytes.Length != size))
											throw new RemoteFileInvalidException("Data length does not match!", path);

										return bytes;
									}
									else
									{
										// Route with progress reporting
										int readLength;
										int readLengthTotal = 0;

										var buffer = new byte[65536]; // 64KiB
										var bufferTotal = new byte[size];

										const double stepUint = 524288D; // 512KiB
										double stepTotal = Math.Ceiling(size / stepUint); // The number of steps to report during downloading
										if (stepTotal < 6)
											stepTotal = 6; // The minimum number of steps

										double stepCurrent = 1D;
										var startTime = DateTime.Now;

										using (var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
										{
											while (readLengthTotal != size)
											{
												// CancellationToken in overload of ReadAsync method will not work for response content.
												var readTask = Task.Run(async () => await stream.ReadAsync(buffer, 0, buffer.Length));
												var timeoutTask = Task.Delay(timeoutDuration);

												var completedTask = await Task.WhenAny(readTask, timeoutTask, monitorTask);
												if (completedTask == timeoutTask)
													throw new TimeoutException("Reading response content timed out!");
												if (completedTask == monitorTask)
													throw new RemoteConnectionLostException("Connection lost!");

												readLength = await readTask;

												if ((readLength == 0) || (readLengthTotal + readLength > size))
													throw new RemoteFileInvalidException("Data length does not match!", path);

												Buffer.BlockCopy(buffer, 0, bufferTotal, readLengthTotal, readLength);

												readLengthTotal += readLength;

												monitorTimer.Change(_monitorInterval, _monitorInterval);

												// Report if read length in total exceeds stepped length.
												if (stepCurrent / stepTotal * size <= readLengthTotal)
												{
													progress.Report(new ProgressInfo(
														currentValue: readLengthTotal,
														totalValue: size,
														elapsedTime: DateTime.Now - startTime,
														isFirst: stepCurrent == 1D));

													stepCurrent++;
												}
											}
										}
										return bufferTotal;
									}
								}
							}
						}
					}
					catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
					{
						// OperationCanceledException includes the case of TaskCanceledException.
						// If cancellation has not been requested, the reason of this exception must be timeout.
						// This is for response header only.
						throw new TimeoutException("Reading response header timed out!");
					}
					catch (ObjectDisposedException ode) when (cancellationToken.IsCancellationRequested)
					{
						// If cancellation has been requested, the reason of this exception must be cancellation.
						// This is for response content only.
						throw new OperationCanceledException("Reading canceled!", ode);
					}
					catch (IOException ie) when (cancellationToken.IsCancellationRequested)
					{
						// If cancellation has been requested while downloading, this exception may be thrown.
						throw new OperationCanceledException("Reading canceled!", ie);
					}
					catch (HttpRequestException hre) when (cancellationToken.IsCancellationRequested)
					{
						// If cancellation has been requested while downloading, this exception may be thrown.
						throw new OperationCanceledException("Reading canceled!", hre);
					}
					catch (HttpRequestException hre) when (hre.InnerException is ObjectDisposedException)
					{
						// If lost connection to FlashAir card, this exception may be thrown.
						// Error message: Error while copying content to a stream.
						throw new RemoteConnectionLostException("Connection lost!");
					}
					catch (HttpRequestException hre) when (hre.InnerException is WebException we)
					{
						// If unable to connect to FlashAir card, this exception will be thrown.
						// The status may vary, such as WebExceptionStatus.NameResolutionFailure,
						// WebExceptionStatus.ConnectFailure.
						throw new RemoteConnectionUnableException(we.Status);
					}
				}
				catch (RemoteConnectionUnableException) when (++retryCount < MaxRetryCount)
				{
					// Wait interval before retry.
					if (TimeSpan.Zero < _retryInterval)
						await Task.Delay(_retryInterval, cancellationToken);
				}
				catch (Exception ex)
				{
					Debug.WriteLine($"Failed to download byte array.\r\n{ex}");
					throw;
				}
			}
		}

		#endregion

		#region Helper

		private static readonly IReadOnlyDictionary<FileManagerCommand, string> _commandMap =
			new Dictionary<FileManagerCommand, string>
			{
				{FileManagerCommand.None, string.Empty},
				{FileManagerCommand.GetFileList, @"command.cgi?op=100&DIR=/"},
				{FileManagerCommand.GetFileNum, @"command.cgi?op=101&DIR=/"},
				{FileManagerCommand.GetThumbnail, @"thumbnail.cgi?/"},
				{FileManagerCommand.GetFirmwareVersion, @"command.cgi?op=108"},
				{FileManagerCommand.GetCid, @"command.cgi?op=120"},
				{FileManagerCommand.GetSsid, @"command.cgi?op=104"},
				{FileManagerCommand.GetUpdateStatus, @"command.cgi?op=102"},
				{FileManagerCommand.GetWriteTimeStamp, @"command.cgi?op=121"},
				{FileManagerCommand.GetUpload, @"command.cgi?op=118"},
				{FileManagerCommand.DeleteFile, @"upload.cgi?DEL=/"},
			};

		/// <summary>
		/// Composes remote path in FlashAir card inserting CGI command string.
		/// </summary>
		/// <param name="command">CGI command type</param>
		/// <param name="remotePath">Source remote path</param>
		/// <returns>Outcome remote path</returns>
		private static string ComposeRemotePath(FileManagerCommand command, string remotePath)
		{
			return string.Concat(Settings.Current.RemoteRoot, _commandMap[command], remotePath.TrimStart('/'));
		}

		private static readonly bool _recordsDownloadString = CommandLine.RecordsDownloadLog
			|| Debugger.IsAttached; // When this application runs in a debugger, download log will be always recorded.

		private const string DownloadFileName = "download.log";

		/// <summary>
		/// Records result of DownloadStringAsync method.
		/// </summary>
		/// <param name="requestPath">Request path</param>
		/// <param name="responseBytes">Response byte array</param>
		private static async Task RecordDownloadStringAsync(string requestPath, byte[] responseBytes)
		{
			var buff = new StringBuilder();
			buff.AppendLine($"request => {requestPath}");
			buff.AppendLine("response -> ");
			buff.AppendLine(Encoding.ASCII.GetString(responseBytes));

			await LogService.RecordAsync(DownloadFileName, buff.ToString());
		}

		#endregion
	}
}