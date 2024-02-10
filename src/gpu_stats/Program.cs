namespace gpustats
{

    using System;
    using System.Runtime.InteropServices;
    using System.Text;
    using MySqlConnector;

    public static class NvmlWrapper
    {
        private const string NvmlLibraryPath = "nvml.dll"; // Ensure this path is correct for your setup

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

    internal class Program
    {
        public static string MySqlConnectionString = $"Server=;User ID=;Password=;Database=;charset=utf8mb4;keepalive=60;minpoolsize=2";

        static async Task Main(string[] args)
        {
            // Initialize NVML
            var result = NvmlWrapper.nvmlInit();
            if (result != 0)
            {
                Console.WriteLine($"Failed to initialize NVML, error code: {result}");
                return;
            }

            // Get the number of NVIDIA devices
            uint deviceCount = NvmlWrapper.GetDeviceCount();
            Console.WriteLine($"Number of NVIDIA GPUs: {deviceCount}");



            for (uint i = 0; i < deviceCount; i++)
            {
                // Get handle for the first GPU
                result = NvmlWrapper.nvmlDeviceGetHandleByIndex(i, out IntPtr device);
                if (result != 0)
                {
                    Console.WriteLine($"Failed to get handle for GPU {i}, error code: {result}");
                    continue;
                }

                // Get the serial number
                string serialNumber = NvmlWrapper.GetSerialNumber(device);
                Console.WriteLine($"GPU {i} Serial Number: {serialNumber}");

                // Get the UUID
                string uuid = NvmlWrapper.GetUUID(device);
                Console.WriteLine($"GPU {i} UUID: {uuid}");
            }

            while (true)
            {
                try
                {

                    using (var SQL = new MySqlConnection(Program.MySqlConnectionString))
                    {
                        await SQL.OpenAsync();

                        for (uint i = 0; i < deviceCount; i++)
                        {
                            // Get handle for the first GPU
                            result = NvmlWrapper.nvmlDeviceGetHandleByIndex(i, out IntPtr device);
                            if (result != 0)
                            {
                                Console.WriteLine($"Failed to get handle for GPU {i}, error code: {result}");
                                continue;
                            }

                            // Get the serial number
                            string serialNumber = NvmlWrapper.GetSerialNumber(device);
                            //Console.WriteLine($"GPU {i} Serial Number: {serialNumber}");

                            // Get the UUID
                            string uuid = NvmlWrapper.GetUUID(device);
                            //Console.WriteLine($"GPU {i} UUID: {uuid}");

                            // Assuming nvmlInit() and nvmlDeviceGetHandleByIndex() have been called successfully and 'device' is your GPU handle

                            // Get Serial Number
                            string gpu_serialNumber = NvmlWrapper.GetSerialNumber(device);

                            // Get UUID
                            string gpu_uuid = NvmlWrapper.GetUUID(device);

                            // Get Temperature
                            NvmlWrapper.nvmlDeviceGetTemperature(device, NvmlWrapper.NvmlTemperatureSensors.NVML_TEMPERATURE_GPU, out uint temp);
                            uint gpu_temperature = temp;

                            // Get GPU Utilization
                            NvmlWrapper.NvmlUtilization gpu_utilization;
                            NvmlWrapper.nvmlDeviceGetUtilizationRates(device, out gpu_utilization);
                            uint gpuUtilization = gpu_utilization.Gpu; // GPU utilization percentage
                            uint memoryUtilization = gpu_utilization.Memory; // Memory utilization percentage

                            // Get Memory Information
                            NvmlWrapper.NvmlMemory memoryInfo;
                            NvmlWrapper.nvmlDeviceGetMemoryInfo(device, out memoryInfo);
                            ulong gpu_memoryTotal = memoryInfo.Total; // Total memory
                            ulong gpu_memoryUsed = memoryInfo.Used; // Used memory
                            ulong gpu_memoryFree = memoryInfo.Free; // Free memory

                            // Get Power Usage
                            NvmlWrapper.nvmlDeviceGetPowerUsage(device, out uint powerUsage);
                            uint gpu_powerUsage = powerUsage; // Convert milliwatts to watts

                            // Get Fan Speed
                            NvmlWrapper.nvmlDeviceGetFanSpeed(device, out uint fanSpeed);
                            uint gpu_fanSpeed = fanSpeed; // Fan speed in percentage

                            // Get Clock Information (for example, SM clock)
                            NvmlWrapper.nvmlDeviceGetClockInfo(device, NvmlWrapper.NvmlClockType.NVML_CLOCK_SM, out uint smClock);
                            uint gpu_smClock = smClock; // SM clock speed in MHz

                            // Get GPU (Graphics) Clock Speed
                            NvmlWrapper.nvmlDeviceGetClockInfo(device, NvmlWrapper.NvmlClockType.NVML_CLOCK_GRAPHICS, out uint graphicsClock);
                            uint gpu_graphicsClock = graphicsClock; // Graphics clock speed in MHz

                            // Get Memory Clock Speed
                            NvmlWrapper.nvmlDeviceGetClockInfo(device, NvmlWrapper.NvmlClockType.NVML_CLOCK_MEM, out uint memoryClock);
                            uint gpu_memoryClock = memoryClock; // Memory clock speed in MHz

                            // Get Power State
                            NvmlWrapper.nvmlDeviceGetPowerState(device, out uint powerState);
                            uint gpu_powerState = powerState; // Power state as a P-state number

                            StringBuilder modelName = new StringBuilder(64); // Adjust size as needed
                            NvmlWrapper.nvmlDeviceGetName(device, modelName, (uint)modelName.Capacity);
                            string gpu_modelName = modelName.ToString();


                            StringBuilder driverVersion = new StringBuilder(64); // Adjust size as needed
                            NvmlWrapper.nvmlSystemGetDriverVersion(driverVersion, (uint)driverVersion.Capacity);
                            string gpu_driverVersion = driverVersion.ToString();

                            // Retrieve PCIe TX bytes (transmitted data)
                            NvmlWrapper.nvmlDeviceGetPcieThroughput(device, NvmlWrapper.NvmlPcieUtilCounter.NVML_PCIE_UTIL_TX_BYTES, out uint pcieTxBytes);
                            uint gpu_pcieTxBytes = pcieTxBytes;

                            // Retrieve PCIe RX bytes (received data)
                            NvmlWrapper.nvmlDeviceGetPcieThroughput(device, NvmlWrapper.NvmlPcieUtilCounter.NVML_PCIE_UTIL_RX_BYTES, out uint pcieRxBytes);
                            uint gpu_pcieRxBytes = pcieRxBytes;


                            using (var cmd = new MySqlCommand())
                            {
                                cmd.Connection = SQL;
                                cmd.CommandText = "INSERT INTO gpu_stats (`when`, uuid, model_name, serial_number, temperature, gpu_utilization, mem_utilization, power_usage, fan_speed, sm_clock, gpu_clock, mem_clock, power_state, mem_total, mem_used, mem_free, pcie_rx, pcie_tx, driver_version) VALUES (NOW(3), @uuid, @model_name, @serial_number, @temperature, @gpu_utilization, @mem_utilization, @power_usage, @fan_speed, @sm_clock, @gpu_clock, @mem_clock, @power_state, @mem_total, @mem_used, @mem_free, @pcie_rx, @pcie_tx, @driver_version)";
                                cmd.Parameters.AddWithValue("uuid", gpu_uuid);
                                cmd.Parameters.AddWithValue("model_name", gpu_modelName);
                                cmd.Parameters.AddWithValue("serial_number", gpu_serialNumber);
                                cmd.Parameters.AddWithValue("temperature", gpu_temperature);
                                cmd.Parameters.AddWithValue("gpu_utilization", gpuUtilization);
                                cmd.Parameters.AddWithValue("mem_utilization", memoryUtilization);
                                cmd.Parameters.AddWithValue("power_usage", gpu_powerUsage);
                                cmd.Parameters.AddWithValue("fan_speed", gpu_fanSpeed);
                                cmd.Parameters.AddWithValue("sm_clock", gpu_smClock);
                                cmd.Parameters.AddWithValue("gpu_clock", gpu_graphicsClock);
                                cmd.Parameters.AddWithValue("mem_clock", gpu_memoryClock);
                                cmd.Parameters.AddWithValue("power_state", gpu_powerState);
                                cmd.Parameters.AddWithValue("mem_total", gpu_memoryTotal);
                                cmd.Parameters.AddWithValue("mem_used", gpu_memoryUsed);
                                cmd.Parameters.AddWithValue("mem_free", gpu_memoryFree);
                                cmd.Parameters.AddWithValue("pcie_rx", gpu_pcieRxBytes);
                                cmd.Parameters.AddWithValue("pcie_tx", gpu_pcieTxBytes);
                                cmd.Parameters.AddWithValue("driver_version", gpu_driverVersion);

                                await cmd.ExecuteNonQueryAsync();
                            }

                        }

                    }
                } catch (Exception ex) {
                    Console.WriteLine(ex.Message);
                }

                await Task.Delay(3000);
            }

            // Shutdown NVML
            NvmlWrapper.nvmlShutdown();
        }
    }
}
