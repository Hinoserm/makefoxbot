using System;
using System.Text;
using System.Runtime.InteropServices;

namespace makefoxsrv
{
    public static class FoxNVMLWrapper
    {
        private const string NvmlLibraryPath = "nvml.dll"; // Ensure this path is correct for your setup

        static FoxNVMLWrapper()
        {
            // Initialize NVML
            int result = nvmlInit();
            if (result != 0)
            {
                throw new Exception($"Failed to initialize NVML, error code: {result}");
            }
        }

        // NVML Initialization and Cleanup
        [DllImport(NvmlLibraryPath, EntryPoint = "nvmlInit_v2")]
        public static extern int nvmlInit();

        [DllImport(NvmlLibraryPath, EntryPoint = "nvmlShutdown")]
        public static extern int nvmlShutdown();

        // Device Handling
        [DllImport(NvmlLibraryPath, EntryPoint = "nvmlDeviceGetHandleByIndex_v2")]
        public static extern int nvmlDeviceGetHandleByIndex(uint index, out IntPtr device);

        // Utilization
        [DllImport(NvmlLibraryPath, EntryPoint = "nvmlDeviceGetUtilizationRates")]
        public static extern int nvmlDeviceGetUtilizationRates(IntPtr device, out NvmlUtilization utilization);

        // Memory Information
        [DllImport(NvmlLibraryPath, EntryPoint = "nvmlDeviceGetMemoryInfo")]
        public static extern int nvmlDeviceGetMemoryInfo(IntPtr device, out NvmlMemory memory);

        // Power Consumption
        [DllImport(NvmlLibraryPath, EntryPoint = "nvmlDeviceGetPowerUsage")]
        public static extern int nvmlDeviceGetPowerUsage(IntPtr device, out uint power);

        // Clock Speeds
        [DllImport(NvmlLibraryPath, EntryPoint = "nvmlDeviceGetClockInfo")]
        public static extern int nvmlDeviceGetClockInfo(IntPtr device, NvmlClockType type, out uint clock);

        // Fan Speed
        [DllImport(NvmlLibraryPath, EntryPoint = "nvmlDeviceGetFanSpeed")]
        public static extern int nvmlDeviceGetFanSpeed(IntPtr device, out uint speed);

        // Temperature
        [DllImport(NvmlLibraryPath, EntryPoint = "nvmlDeviceGetTemperature")]
        public static extern int nvmlDeviceGetTemperature(IntPtr device, NvmlTemperatureSensors sensorType, out uint temp);

        // ECC Errors
        [DllImport(NvmlLibraryPath, EntryPoint = "nvmlDeviceGetTotalEccErrors")]
        public static extern int nvmlDeviceGetTotalEccErrors(IntPtr device, NvmlMemoryErrorType errorType, NvmlEccCounterType counterType, out ulong count);

        // Performance State
        [DllImport(NvmlLibraryPath, EntryPoint = "nvmlDeviceGetPowerState")]
        public static extern int nvmlDeviceGetPowerState(IntPtr device, out uint pState);

        // PCIe Throughput
        [DllImport(NvmlLibraryPath, EntryPoint = "nvmlDeviceGetPcieThroughput")]
        public static extern int nvmlDeviceGetPcieThroughput(IntPtr device, NvmlPcieUtilCounter counter, out uint value);

        // Structures
        [StructLayout(LayoutKind.Sequential)]
        public struct NvmlMemory
        {
            public ulong Total;
            public ulong Free;
            public ulong Used;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct NvmlUtilization
        {
            public uint Gpu;
            public uint Memory;
        }

        // Enums
        public enum NvmlClockType
        {
            NVML_CLOCK_GRAPHICS = 0,
            NVML_CLOCK_SM = 1,
            NVML_CLOCK_MEM = 2,
            NVML_CLOCK_VIDEO = 3
        }

        public enum NvmlTemperatureSensors
        {
            NVML_TEMPERATURE_GPU = 0
        }

        public enum NvmlMemoryErrorType
        {
            NVML_MEMORY_ERROR_TYPE_CORRECTED = 0,
            NVML_MEMORY_ERROR_TYPE_UNCORRECTED = 1
        }

        public enum NvmlEccCounterType
        {
            NVML_VOLATILE_ECC = 0,
            NVML_AGGREGATE_ECC = 1
        }

        public enum NvmlPcieUtilCounter
        {
            NVML_PCIE_UTIL_TX_BYTES = 0,
            NVML_PCIE_UTIL_RX_BYTES = 1
        }

        // Device Information
        [DllImport(NvmlLibraryPath, EntryPoint = "nvmlDeviceGetSerial")]
        public static extern int nvmlDeviceGetSerial(IntPtr device, StringBuilder serial, uint length);

        [DllImport(NvmlLibraryPath, EntryPoint = "nvmlDeviceGetUUID")]
        public static extern int nvmlDeviceGetUUID(IntPtr device, StringBuilder uuid, uint length);

        [DllImport(NvmlLibraryPath, EntryPoint = "nvmlDeviceGetCount_v2")]
        public static extern int nvmlDeviceGetCount(out uint deviceCount);

        [DllImport(NvmlLibraryPath, EntryPoint = "nvmlDeviceGetName")]
        public static extern int nvmlDeviceGetName(IntPtr device, StringBuilder name, uint length);

        [DllImport(NvmlLibraryPath, EntryPoint = "nvmlSystemGetDriverVersion")]
        public static extern int nvmlSystemGetDriverVersion(StringBuilder version, uint length);


        // Helper Methods
        public static string GetSerialNumber(IntPtr device)
        {
            StringBuilder serial = new StringBuilder(30); // Adjust size as needed
            var result = nvmlDeviceGetSerial(device, serial, (uint)serial.Capacity);
            if (result == 0)
            {
                return serial.ToString();
            }
            return "";
        }

        public static string GetUUID(IntPtr device)
        {
            StringBuilder uuid = new StringBuilder(80); // Adjust size as needed
            var result = nvmlDeviceGetUUID(device, uuid, (uint)uuid.Capacity);
            if (result == 0)
            {
                return uuid.ToString();
            }
            throw new Exception("Failed to get UUID, error code: " + result);
        }

        public static uint GetDeviceCount()
        {
            nvmlDeviceGetCount(out var count);
            return count;
        }

        public static void ListNvidiaGPUs()
        {
            var count = GetDeviceCount();
            Console.WriteLine($"Number of NVIDIA GPUs: {count}");
            for (uint i = 0; i < count; i++)
            {
                nvmlDeviceGetHandleByIndex(i, out var device);
                var serialNumber = GetSerialNumber(device);
                var uuid = GetUUID(device);
                Console.WriteLine($"GPU {i}: Serial Number: {serialNumber}, UUID: {uuid}");
            }
        }

    }
}
