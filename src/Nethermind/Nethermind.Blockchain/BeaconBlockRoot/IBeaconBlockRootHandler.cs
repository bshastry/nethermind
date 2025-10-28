// SPDX-FileCopyrightText: 2023 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using Nethermind.Core;
using Nethermind.Core.Eip2930;
using Nethermind.Core.Specs;
using Nethermind.Evm;
using Nethermind.Evm.Tracing;

namespace Nethermind.Blockchain.BeaconBlockRoot;
public interface IBeaconBlockRootHandler : IHasAccessList
{
    (Address? toAddress, AccessList? accessList) BeaconRootsAccessList(Block block, IReleaseSpec spec, bool includeStorageCells = true);
    void StoreBeaconRoot(Block block, IReleaseSpec spec, ITxTracer tracer);

    /// <summary>
    /// Stores the beacon root and returns the gas used by the system call.
    /// </summary>
    /// <param name="block">The block containing the beacon root.</param>
    /// <param name="spec">The release spec.</param>
    /// <param name="tracer">The transaction tracer.</param>
    /// <returns>The actual gas used by the beacon root storage system call, or 0 if no storage occurred.</returns>
    long StoreBeaconRootWithGas(Block block, IReleaseSpec spec, ITxTracer tracer);
}
