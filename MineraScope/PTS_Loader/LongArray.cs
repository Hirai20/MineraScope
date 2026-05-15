using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace MineraScope
{
    public class LongArray<T> : IDisposable where T : struct
    {
        private IntPtr _head;

        private long _capacity;

        private ulong _bytes;

        private int _elementSize;

        protected bool disposed;

        public T this[long index]
        {
            get
            {
                return (T)Marshal.PtrToStructure(_getAddress(index), typeof(T));
            }
            set
            {
                IntPtr ptr = _getAddress(index);
                Marshal.StructureToPtr(value, ptr, fDeleteOld: true);
            }
        }

        public LongArray(long capacity)
        {
            if (_capacity < 0)
            {
                throw new ArgumentException("The capacity can not be negative");
            }
            _elementSize = Marshal.SizeOf(default(T));
            _capacity = capacity;
            _bytes = (ulong)(capacity * _elementSize);
            _head = Marshal.AllocHGlobal((IntPtr)(long)_bytes);
        }

        public IntPtr GetIntPtr()
        {
            return _head;
        }

        public void Dispose()
        {
            if (!disposed)
            {
                Marshal.FreeHGlobal(_head);
                disposed = true;
            }
        }

        protected IntPtr _getAddress(long index)
        {
            if (disposed)
            {
                throw new ObjectDisposedException("Can't access the array once it has been disposed!");
            }
            if (index < 0)
            {
                throw new IndexOutOfRangeException("Negative indices are not allowed");
            }
            if (index >= _capacity)
            {
                throw new IndexOutOfRangeException("Index is out of bounds of this array");
            }
            return (IntPtr)((long)_head + index * _elementSize);
        }
    }

}
