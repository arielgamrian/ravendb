using System;
using Voron.Impl.Paging;

namespace Voron.Impl.Journal
{
    public unsafe interface IJournalWriter : IDisposable
    {
        void Write(long position, byte* p, int numberOf4Kb);
        int NumberOfAllocated4Kb { get;  }
        bool Disposed { get; }
        bool DeleteOnClose { get; set; }
        AbstractPager CreatePager();
        bool Read(long positionBy4Kb, byte* buffer, int countBy4Kb);
        void Truncate(long size);
    }
}