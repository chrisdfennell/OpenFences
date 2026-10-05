using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Pickets.Services
{
    /// <summary>
    /// One Windows performance counter (PDH), by its English path so it works in every language,
    /// e.g. "\PhysicalDisk(_Total)\Disk Read Bytes/sec" or, with a * instance, one value per
    /// instance ("\GPU Engine(*)\Utilization Percentage"). Rates need two readings, so the first
    /// <see cref="Collect"/> after opening gives no values yet.
    /// </summary>
    internal sealed class PdhCounter : IDisposable
    {
        private IntPtr _query, _counter;

        private PdhCounter(IntPtr query, IntPtr counter)
        {
            _query = query;
            _counter = counter;
        }

        /// <summary>Null if the counter doesn't exist on this PC (or its counters are turned off).</summary>
        public static PdhCounter? TryOpen(string englishPath)
        {
            try
            {
                if (PdhOpenQueryW(null, IntPtr.Zero, out var query) != 0) return null;
                if (PdhAddEnglishCounterW(query, englishPath, IntPtr.Zero, out var counter) != 0)
                {
                    PdhCloseQuery(query);
                    return null;
                }
                PdhCollectQueryData(query); // the first sample for rates
                return new PdhCounter(query, counter);
            }
            catch (DllNotFoundException) { return null; }
            catch (EntryPointNotFoundException) { return null; }
        }

        /// <summary>Take a new sample. False if that failed.</summary>
        public bool Collect() => _query != IntPtr.Zero && PdhCollectQueryData(_query) == 0;

        /// <summary>The value for a counter without a * instance, or null if there isn't one yet.</summary>
        public double? Value()
        {
            if (_counter == IntPtr.Zero) return null;
            if (PdhGetFormattedCounterValue(_counter, Format, IntPtr.Zero, out var v) != 0) return null;
            return v.CStatus is 0 or 1 ? v.DoubleValue : null;
        }

        /// <summary>Every instance's value for a counter with a * instance (instances without a
        /// value yet are left out).</summary>
        public List<(string Instance, double Value)> Values()
        {
            var result = new List<(string, double)>();
            if (_counter == IntPtr.Zero) return result;

            uint size = 0;
            int status = PdhGetFormattedCounterArrayW(_counter, Format, ref size, out _, IntPtr.Zero);
            if (status != PDH_MORE_DATA || size == 0) return result;

            var buffer = Marshal.AllocHGlobal((int)size);
            try
            {
                if (PdhGetFormattedCounterArrayW(_counter, Format, ref size, out uint count, buffer) != 0) return result;
                int itemSize = Marshal.SizeOf<PDH_FMT_COUNTERVALUE_ITEM>();
                for (int i = 0; i < count; i++)
                {
                    var item = Marshal.PtrToStructure<PDH_FMT_COUNTERVALUE_ITEM>(buffer + i * itemSize);
                    if (item.Value.CStatus is not (0 or 1)) continue;
                    result.Add((Marshal.PtrToStringUni(item.Name) ?? "", item.Value.DoubleValue));
                }
            }
            finally { Marshal.FreeHGlobal(buffer); }
            return result;
        }

        public void Dispose()
        {
            if (_query != IntPtr.Zero) PdhCloseQuery(_query);
            _query = _counter = IntPtr.Zero;
        }

        // ---------- Win32 ----------
        private const uint PDH_FMT_DOUBLE = 0x00000200;
        private const uint PDH_FMT_NOCAP100 = 0x00008000;
        private const uint Format = PDH_FMT_DOUBLE | PDH_FMT_NOCAP100;
        private const int PDH_MORE_DATA = unchecked((int)0x800007D2);

        [StructLayout(LayoutKind.Explicit)]
        private struct PDH_FMT_COUNTERVALUE
        {
            [FieldOffset(0)] public uint CStatus;
            [FieldOffset(8)] public double DoubleValue;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct PDH_FMT_COUNTERVALUE_ITEM
        {
            public IntPtr Name;
            public PDH_FMT_COUNTERVALUE Value;
        }

        [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
        private static extern int PdhOpenQueryW(string? dataSource, IntPtr userData, out IntPtr query);

        [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
        private static extern int PdhAddEnglishCounterW(IntPtr query, string path, IntPtr userData, out IntPtr counter);

        [DllImport("pdh.dll")]
        private static extern int PdhCollectQueryData(IntPtr query);

        [DllImport("pdh.dll")]
        private static extern int PdhGetFormattedCounterValue(IntPtr counter, uint format, IntPtr type, out PDH_FMT_COUNTERVALUE value);

        [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
        private static extern int PdhGetFormattedCounterArrayW(IntPtr counter, uint format, ref uint bufferSize, out uint itemCount, IntPtr itemBuffer);

        [DllImport("pdh.dll")]
        private static extern int PdhCloseQuery(IntPtr query);
    }
}
