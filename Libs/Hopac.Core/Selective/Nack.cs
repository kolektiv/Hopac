﻿// Copyright (C) by Housemarque, Inc.

namespace Hopac.Core {
  using Microsoft.FSharp.Core;
  using System.Diagnostics;
  using System.Runtime.CompilerServices;
  using System.Threading;
  using System;

  internal sealed class Nack : Alt<Unit> {
    internal Nack Next;
    internal volatile int State;
    internal int I0;
    internal int I1;
    internal Cont<Unit> Takers;

    internal const int Locked = -1;
    internal const int Initial = 0;
    internal const int Signaled = 1;

    [MethodImpl(AggressiveInlining.Flag)]
    internal Nack(Nack next, int i0) {
      this.Next = next;
      this.I0 = i0;
      this.I1 = Int32.MaxValue;
    }

    internal override void DoJob(ref Worker wr, Cont<Unit> uK) {
    Spin:
      var state = this.State;
      if (state > Initial) goto Signaled;
      if (state < Initial) goto Spin;
      if (Initial != Interlocked.CompareExchange(ref this.State, ~state, state)) goto Spin;

      WaitQueue.AddTaker(ref this.Takers, uK);
      this.State = Initial;
      return;

    Signaled:
      Work.Do(uK, ref wr);
    }

    internal override void TryAlt(ref Worker wr, int i, Cont<Unit> uK, Else uE) {
      var pkSelf = uE.pk;
    Spin:
      var state = this.State;
      if (state > Initial) goto TryPick;
      if (state < Initial) goto Spin;
      if (Initial != Interlocked.CompareExchange(ref this.State, ~state, state)) goto Spin;

      WaitQueue.AddTaker(ref this.Takers, i, pkSelf, uK);
      this.State = Initial;
      uE.TryElse(ref wr, i + 1);
      return;

    TryPick:
      var st = Pick.TryPick(pkSelf);
      if (st > 0) goto AlreadyPicked;
      if (st < 0) goto TryPick;

      Pick.SetNacks(ref wr, i, pkSelf);

      Work.Do(uK, ref wr);
    AlreadyPicked:
      return;
    }

    [MethodImpl(AggressiveInlining.Flag)]
    internal static void Signal(ref Worker wr, Nack nk) {
    Spin:
      var state = nk.State;
      if (state > Initial) goto JustExit; // XXX Can this happen?
      if (state < Initial) goto Spin;
      if (Initial != Interlocked.CompareExchange(ref nk.State, state+1, state)) goto Spin;

      var takers = nk.Takers;
      if (null == takers)
        goto JustExit;
      nk.Takers = null;
      var me = 0;
      Work cursor = takers;

    TryTaker:
      var taker = cursor as Cont<Unit>;
      cursor = cursor.Next;
      var pk = taker.GetPick(ref me);
      if (null == pk)
        goto GotTaker;

    TryPick:
      var st = Pick.TryPick(pk);
      if (st > 0) goto TryNextTaker;
      if (st < 0) goto TryPick;

      Pick.SetNacks(ref wr, me, pk);
    GotTaker:
      Worker.Push(ref wr, taker);
    TryNextTaker:
      if (cursor != takers) goto TryTaker;
    JustExit:
      return;
    }
  }
}
