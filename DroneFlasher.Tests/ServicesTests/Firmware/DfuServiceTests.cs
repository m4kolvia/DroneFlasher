using DroneFlasher.Models;
using DroneFlasher.Services.Firmware;
using LibUsbDotNet.LibUsb;
using LibUsbDotNet.Main;
using System.Diagnostics;
using Xunit;

namespace DroneFlasher.Tests.ServicesTests
{
    public class DfuServiceTests
    {
        private class FakeUsbClient : IDfuUsbClient
        {
            public bool IsDevicePresentResult { get; set; } = true;

            public bool IsDevicePresent() => IsDevicePresentResult;

            public Task ClearStatusAsync(IUsbDevice device, IProgress<LogEntry>? log = null, CancellationToken ct = default) => Task.CompletedTask;
            public Task<DfuStatusInfo> GetStatusAsync(IUsbDevice device, CancellationToken ct = default) => Task.FromResult(new DfuStatusInfo(0, 0, 0, 0));
            public Task ExecuteMassEraseAsync(IUsbDevice device, IProgress<LogEntry>? log = null, CancellationToken ct = default) => Task.CompletedTask;
            public Task SetAddressAsync(IUsbDevice device, uint address, CancellationToken ct = default) => Task.CompletedTask;
            public Task DownloadBlockAsync(IUsbDevice device, ushort blockNumber, byte[] data, CancellationToken ct = default) => Task.CompletedTask;
            public Task LeaveDfuAsync(IUsbDevice device, IProgress<LogEntry>? log = null, CancellationToken ct = default) => Task.CompletedTask;
        }

        [Fact]
        public async Task EnterBootloaderAndWaitAsync_DeviceAlreadyPresent_ReturnsTrueImmediately()
        {
            // Arrange
            var fakeUsbClient = new FakeUsbClient { IsDevicePresentResult = true };
            var service = new DfuService(fakeUsbClient); 

            // Act
            var stopwatch = Stopwatch.StartNew();
            bool result = await service.EnterBootloaderAndWaitAsync(null);
            stopwatch.Stop();

            // Assert
            Assert.True(result); 
            Assert.True(stopwatch.ElapsedMilliseconds < 500); 
        }

        [Fact]
        public async Task EnterBootloaderAndWaitAsync_CancellationRequested_ThrowsOperationCanceledException()
        {
            // Arrange
            var fakeUsbClient = new FakeUsbClient { IsDevicePresentResult = false }; 
            var service = new DfuService(fakeUsbClient);
            var cts = new CancellationTokenSource();

            cts.CancelAfter(150);

            // Act & Assert
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                service.EnterBootloaderAndWaitAsync(null, null, null, cts.Token));
        }
    }
}