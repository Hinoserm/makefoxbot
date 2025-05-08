using System;
using System.Text;
using System.Runtime.InteropServices;

namespace makefoxsrv
{
    public static class FoxNVMLWrapper
    {
        private static readonly IntPtr _libHandle;

        static FoxNVMLWrapper()
        {
            string libName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? "nvml.dll"
                : RuntimeInformation.IsOSPlatform(OSPlatform.Linux)
                    ? "libnvidia-ml.so.1"
                    : throw new PlatformNotSupportedException("NVML not supported on this OS");

            if (!NativeLibrary.TryLoad(libName, out _libHandle))
                throw new DllNotFoundException($"Could not load {libName}");

            // bind delegates
            _nvmlInit = Load<nvmlInitDelegate>("nvmlInit_v2");
            _nvmlShutdown = Load<nvmlShutdownDelegate>("nvmlShutdown");
            _nvmlDeviceGetCount = Load<nvmlDeviceGetCountDelegate>("nvmlDeviceGetCount_v2");
            _nvmlDeviceGetHandleByIndex = Load<nvmlDeviceGetHandleByIndexDelegate>("nvmlDeviceGetHandleByIndex_v2");
            _nvmlDeviceGetUtilizationRates = Load<nvmlDeviceGetUtilizationRatesDelegate>("nvmlDeviceGetUtilizationRates");
            _nvmlDeviceGetMemoryInfo = Load<nvmlDeviceGetMemoryInfoDelegate>("nvmlDeviceGetMemoryInfo");
            _nvmlDeviceGetPowerUsage = Load<nvmlDeviceGetPowerUsageDelegate>("nvmlDeviceGetPowerUsage");
            _nvmlDeviceGetClockInfo = Load<nvmlDeviceGetClockInfoDelegate>("nvmlDeviceGetClockInfo");
            _nvmlDeviceGetFanSpeed = Load<nvmlDeviceGetFanSpeedDelegate>("nvmlDeviceGetFanSpeed");
            _nvmlDeviceGetTemperature = Load<nvmlDeviceGetTemperatureDelegate>("nvmlDeviceGetTemperature");
            _nvmlDeviceGetTotalEccErrors = Load<nvmlDeviceGetTotalEccErrorsDelegate>("nvmlDeviceGetTotalEccErrors");
            _nvmlDeviceGetPowerState = Load<nvmlDeviceGetPowerStateDelegate>("nvmlDeviceGetPowerState");
            _nvmlDeviceGetPcieThroughput = Load<nvmlDeviceGetPcieThroughputDelegate>("nvmlDeviceGetPcieThroughput");
            _nvmlDeviceGetSerial = Load<nvmlDeviceGetSerialDelegate>("nvmlDeviceGetSerial");
            _nvmlDeviceGetUUID = Load<nvmlDeviceGetUUIDDelegate>("nvmlDeviceGetUUID");
            _nvmlDeviceGetName = Load<nvmlDeviceGetNameDelegate>("nvmlDeviceGetName");
            _nvmlSystemGetDriverVersion = Load<nvmlSystemGetDriverVersionDelegate>("nvmlSystemGetDriverVersion");

            int initResult = _nvmlInit();
            if (initResult != (int)NvmlReturn.Success)
                throw new NvmlException((NvmlReturn)initResult);
        }

        private static T Load<T>(string symbol) where T : Delegate
        {
            IntPtr ptr = NativeLibrary.GetExport(_libHandle, symbol);
            return Marshal.GetDelegateForFunctionPointer<T>(ptr);
        }

        // Native delegates
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int nvmlInitDelegate();
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int nvmlShutdownDelegate();
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int nvmlDeviceGetCountDelegate(out uint count);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int nvmlDeviceGetHandleByIndexDelegate(uint idx, out IntPtr dev);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int nvmlDeviceGetUtilizationRatesDelegate(IntPtr dev, out NativeUtilization u);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int nvmlDeviceGetMemoryInfoDelegate(IntPtr dev, out NativeMemory m);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int nvmlDeviceGetPowerUsageDelegate(IntPtr dev, out uint p);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int nvmlDeviceGetClockInfoDelegate(IntPtr dev, NvmlClockType t, out uint c);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int nvmlDeviceGetFanSpeedDelegate(IntPtr dev, out uint s);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int nvmlDeviceGetTemperatureDelegate(IntPtr dev, NvmlTemperatureSensors s, out uint t);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int nvmlDeviceGetTotalEccErrorsDelegate(IntPtr dev, NvmlMemoryErrorType e, NvmlEccCounterType c, out ulong cnt);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int nvmlDeviceGetPowerStateDelegate(IntPtr dev, out uint ps);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int nvmlDeviceGetPcieThroughputDelegate(IntPtr dev, NvmlPcieUtilCounter ctr, out uint v);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int nvmlDeviceGetSerialDelegate(IntPtr dev, StringBuilder sb, uint len);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int nvmlDeviceGetUUIDDelegate(IntPtr dev, StringBuilder sb, uint len);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int nvmlDeviceGetNameDelegate(IntPtr dev, StringBuilder sb, uint len);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int nvmlSystemGetDriverVersionDelegate(StringBuilder sb, uint len);

        // Delegate instances
        private static nvmlInitDelegate _nvmlInit;
        private static nvmlShutdownDelegate _nvmlShutdown;
        private static nvmlDeviceGetCountDelegate _nvmlDeviceGetCount;
        private static nvmlDeviceGetHandleByIndexDelegate _nvmlDeviceGetHandleByIndex;
        private static nvmlDeviceGetUtilizationRatesDelegate _nvmlDeviceGetUtilizationRates;
        private static nvmlDeviceGetMemoryInfoDelegate _nvmlDeviceGetMemoryInfo;
        private static nvmlDeviceGetPowerUsageDelegate _nvmlDeviceGetPowerUsage;
        private static nvmlDeviceGetClockInfoDelegate _nvmlDeviceGetClockInfo;
        private static nvmlDeviceGetFanSpeedDelegate _nvmlDeviceGetFanSpeed;
        private static nvmlDeviceGetTemperatureDelegate _nvmlDeviceGetTemperature;
        private static nvmlDeviceGetTotalEccErrorsDelegate _nvmlDeviceGetTotalEccErrors;
        private static nvmlDeviceGetPowerStateDelegate _nvmlDeviceGetPowerState;
        private static nvmlDeviceGetPcieThroughputDelegate _nvmlDeviceGetPcieThroughput;
        private static nvmlDeviceGetSerialDelegate _nvmlDeviceGetSerial;
        private static nvmlDeviceGetUUIDDelegate _nvmlDeviceGetUUID;
        private static nvmlDeviceGetNameDelegate _nvmlDeviceGetName;
        private static nvmlSystemGetDriverVersionDelegate _nvmlSystemGetDriverVersion;

        // Public API
        public static void ShutdownNVML()
        {
            int r = _nvmlShutdown();
            if (r != (int)NvmlReturn.Success) throw new NvmlException((NvmlReturn)r);
        }

        public static GpuDevice[] GetAllDevices()
        {
            int r = _nvmlDeviceGetCount(out uint count);
            if (r != (int)NvmlReturn.Success) throw new NvmlException((NvmlReturn)r);

            var devices = new GpuDevice[count];
            for (uint i = 0; i < count; i++)
            {
                r = _nvmlDeviceGetHandleByIndex(i, out IntPtr handle);
                if (r != (int)NvmlReturn.Success) throw new NvmlException((NvmlReturn)r);
                devices[i] = new GpuDevice(handle, i);
            }
            return devices;
        }

        public static string GetDriverVersion()
        {
            var sb = new StringBuilder(80);
            int r = _nvmlSystemGetDriverVersion(sb, (uint)sb.Capacity);
            if (r != (int)NvmlReturn.Success) throw new NvmlException((NvmlReturn)r);
            return sb.ToString();
        }

        public sealed class GpuDevice
        {
            private readonly IntPtr _handle;
            public uint Index { get; }

            internal GpuDevice(IntPtr handle, uint index)
            {
                _handle = handle;
                Index = index;
            }

            public string Name
            {
                get
                {
                    var sb = new StringBuilder(64);
                    int r = _nvmlDeviceGetName(_handle, sb, (uint)sb.Capacity);
                    if (r != (int)NvmlReturn.Success) throw new NvmlException((NvmlReturn)r);
                    return sb.ToString();
                }
            }

            public string SerialNumber
            {
                get
                {
                    var sb = new StringBuilder(30);
                    int r = _nvmlDeviceGetSerial(_handle, sb, (uint)sb.Capacity);
                    if (r != (int)NvmlReturn.Success) throw new NvmlException((NvmlReturn)r);
                    return sb.ToString();
                }
            }

            public string UUID
            {
                get
                {
                    var sb = new StringBuilder(80);
                    int r = _nvmlDeviceGetUUID(_handle, sb, (uint)sb.Capacity);
                    if (r != (int)NvmlReturn.Success) throw new NvmlException((NvmlReturn)r);
                    return sb.ToString();
                }
            }

            public UtilizationInfo Utilization
            {
                get
                {
                    int r = _nvmlDeviceGetUtilizationRates(_handle, out NativeUtilization u);
                    if (r != (int)NvmlReturn.Success) throw new NvmlException((NvmlReturn)r);
                    return new UtilizationInfo(u.Gpu, u.Memory);
                }
            }

            public MemoryInfo Memory
            {
                get
                {
                    int r = _nvmlDeviceGetMemoryInfo(_handle, out NativeMemory m);
                    if (r != (int)NvmlReturn.Success) throw new NvmlException((NvmlReturn)r);
                    return new MemoryInfo(m.Total, m.Free, m.Used);
                }
            }

            public uint PowerUsage
            {
                get
                {
                    int r = _nvmlDeviceGetPowerUsage(_handle, out uint p);
                    if (r != (int)NvmlReturn.Success) throw new NvmlException((NvmlReturn)r);
                    return p;
                }
            }

            public uint GraphicsClock => GetClock(NvmlClockType.NVML_CLOCK_GRAPHICS);
            public uint SMClock => GetClock(NvmlClockType.NVML_CLOCK_SM);
            public uint MemClock => GetClock(NvmlClockType.NVML_CLOCK_MEM);
            public uint VideoClock => GetClock(NvmlClockType.NVML_CLOCK_VIDEO);

            public uint FanSpeed
            {
                get
                {
                    int r = _nvmlDeviceGetFanSpeed(_handle, out uint s);
                    if (r != (int)NvmlReturn.Success) throw new NvmlException((NvmlReturn)r);
                    return s;
                }
            }

            public uint Temperature
            {
                get
                {
                    int r = _nvmlDeviceGetTemperature(_handle, NvmlTemperatureSensors.NVML_TEMPERATURE_GPU, out uint t);
                    if (r != (int)NvmlReturn.Success) throw new NvmlException((NvmlReturn)r);
                    return t;
                }
            }

            public ulong TotalEccErrors(NvmlMemoryErrorType e, NvmlEccCounterType c)
            {
                int r = _nvmlDeviceGetTotalEccErrors(_handle, e, c, out ulong cnt);
                if (r != (int)NvmlReturn.Success) throw new NvmlException((NvmlReturn)r);
                return cnt;
            }

            public uint PowerState
            {
                get
                {
                    int r = _nvmlDeviceGetPowerState(_handle, out uint ps);
                    if (r != (int)NvmlReturn.Success) throw new NvmlException((NvmlReturn)r);
                    return ps;
                }
            }

            public uint PcieTx
            {
                get
                {
                    int r = _nvmlDeviceGetPcieThroughput(_handle, NvmlPcieUtilCounter.NVML_PCIE_UTIL_TX_BYTES, out uint v);
                    if (r != (int)NvmlReturn.Success) throw new NvmlException((NvmlReturn)r);
                    return v;
                }
            }

            public uint PcieRx
            {
                get
                {
                    int r = _nvmlDeviceGetPcieThroughput(_handle, NvmlPcieUtilCounter.NVML_PCIE_UTIL_RX_BYTES, out uint v);
                    if (r != (int)NvmlReturn.Success) throw new NvmlException((NvmlReturn)r);
                    return v;
                }
            }

            private uint GetClock(NvmlClockType type)
            {
                int r = _nvmlDeviceGetClockInfo(_handle, type, out uint c);
                if (r != (int)NvmlReturn.Success) throw new NvmlException((NvmlReturn)r);
                return c;
            }
        }

        // Public records & enums
        public record MemoryInfo(ulong Total, ulong Free, ulong Used);
        public record UtilizationInfo(uint Gpu, uint Memory);

        public enum NvmlClockType { NVML_CLOCK_GRAPHICS = 0, NVML_CLOCK_SM = 1, NVML_CLOCK_MEM = 2, NVML_CLOCK_VIDEO = 3 }
        public enum NvmlTemperatureSensors { NVML_TEMPERATURE_GPU = 0 }
        public enum NvmlMemoryErrorType { NVML_MEMORY_ERROR_TYPE_CORRECTED = 0, NVML_MEMORY_ERROR_TYPE_UNCORRECTED = 1 }
        public enum NvmlEccCounterType { NVML_VOLATILE_ECC = 0, NVML_AGGREGATE_ECC = 1 }
        public enum NvmlPcieUtilCounter { NVML_PCIE_UTIL_TX_BYTES = 0, NVML_PCIE_UTIL_RX_BYTES = 1 }

        public enum NvmlReturn
        {
            Success = 0,
            Uninitialized = 1,
            InvalidArgument = 2,
            NotSupported = 3,
            NoPermission = 4,
            AlreadyInitialized = 5,
            NotFound = 6,
            InsufficientSize = 7,
            InsufficientPower = 8,
            DriverNotLoaded = 9,
            Timeout = 10,
            IrqIssue = 11,
            LibraryNotFound = 12,
            FunctionNotFound = 13,
            CorruptedInforom = 14,
            GpuIsLost = 15,
            ResetRequired = 16,
            OperatingSystem = 17,
            LibRmVersionMismatch = 18,
            InUse = 19,
            Memory = 20,
            NoData = 21,
            VgpuEccNotSupported = 22,
            InsufficientResources = 23,
            Unknown = 999
        }

        public class NvmlException : Exception
        {
            public NvmlReturn ErrorCode { get; }
            public NvmlException(NvmlReturn code) : base($"NVML call failed: {code}") => ErrorCode = code;
        }

        // Private native structs
        [StructLayout(LayoutKind.Sequential)] private struct NativeMemory { public ulong Total, Free, Used; }
        [StructLayout(LayoutKind.Sequential)] private struct NativeUtilization { public uint Gpu, Memory; }
    }
}
