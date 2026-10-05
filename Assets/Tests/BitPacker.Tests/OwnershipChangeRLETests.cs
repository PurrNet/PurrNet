using System;
using NUnit.Framework;
using PurrNet;
using PurrNet.Modules;
using PurrNet.Packing;
using PurrNet.Pooling;
using PurrNet.Utils;

public class OwnershipChangeRLETests
{
	private BitPacker packer;

	[SetUp]
	public void Setup()
	{
		Hasher.ClearState();
		NetworkManager.CallAllRegisters();

		packer = BitPackerPool.Get();
	}

	[TearDown]
	public void Teardown()
	{
		packer?.Dispose();
	}

	static OwnershipChange MakeChange(DisposableList<NetworkID> ids, PlayerID player, bool isAdding = false)
	{
		return new OwnershipChange
		{
			sceneId = default,
			identities = ids,
			isAdding = isAdding,
			player = player,
			isSpawner = false
		};
	}

	[Test]
	public void ContiguousRun_RoundTrips()
	{
		var scope = new PlayerID(1, false);

		using var ids = DisposableList<NetworkID>.Create(4);
		ids.Add(new NetworkID(100, scope));
		ids.Add(new NetworkID(101, scope));
		ids.Add(new NetworkID(102, scope));
		ids.Add(new NetworkID(103, scope));

		var original = MakeChange(ids, scope);

		packer.ResetPositionAndMode(false);
		Packer<OwnershipChange>.Write(packer, original);

		packer.ResetPositionAndMode(true);
		OwnershipChange result = default;
		Packer<OwnershipChange>.Read(packer, ref result);

		Assert.AreEqual(4, result.identities.Count);
		for (int i = 0; i < 4; i++)
			Assert.AreEqual(ids[i], result.identities[i]);

		Assert.AreEqual(original.isAdding, result.isAdding);
		Assert.AreEqual(original.player, result.player);
		Assert.AreEqual(original.isSpawner, result.isSpawner);
		Assert.AreEqual(original.sceneId, result.sceneId);

		result.identities.Dispose();
	}

	[Test]
	public void RunWithGap_RoundTrips()
	{
		var scope = new PlayerID(2, false);

		// Simulates a hierarchy despawn where one child in the middle
		// was skipped (eg already had no owner), breaking the run
		using var ids = DisposableList<NetworkID>.Create(5);
		ids.Add(new NetworkID(200, scope));
		ids.Add(new NetworkID(201, scope));
		ids.Add(new NetworkID(203, scope));
		ids.Add(new NetworkID(204, scope));
		ids.Add(new NetworkID(205, scope));

		var original = MakeChange(ids, scope);

		packer.ResetPositionAndMode(false);
		Packer<OwnershipChange>.Write(packer, original);

		packer.ResetPositionAndMode(true);
		OwnershipChange result = default;
		Packer<OwnershipChange>.Read(packer, ref result);

		Assert.AreEqual(5, result.identities.Count);
		for (int i = 0; i < 5; i++)
			Assert.AreEqual(ids[i], result.identities[i]);

		result.identities.Dispose();
	}

	[Test]
	public void DifferentScopes_DoNotMergeIntoOneRun()
	{
		var scopeA = new PlayerID(3, false);
		var scopeB = new PlayerID(4, false);

		// Same numeric ids but different scopes
		// must not be treated as one run
		using var ids = DisposableList<NetworkID>.Create(2);
		ids.Add(new NetworkID(50, scopeA));
		ids.Add(new NetworkID(51, scopeB));

		var original = MakeChange(ids, scopeA);

		packer.ResetPositionAndMode(false);
		Packer<OwnershipChange>.Write(packer, original);

		packer.ResetPositionAndMode(true);
		OwnershipChange result = default;
		Packer<OwnershipChange>.Read(packer, ref result);

		Assert.AreEqual(2, result.identities.Count);
		Assert.AreEqual(ids[0], result.identities[0]);
		Assert.AreEqual(ids[1], result.identities[1]);

		result.identities.Dispose();
	}

	[Test]
	public void SingleIdentity_RoundTrips()
	{
		var scope = new PlayerID(5, false);

		using var ids = DisposableList<NetworkID>.Create(1);
		ids.Add(new NetworkID(999, scope));

		var original = MakeChange(ids, scope, isAdding: true);

		packer.ResetPositionAndMode(false);
		Packer<OwnershipChange>.Write(packer, original);

		packer.ResetPositionAndMode(true);
		OwnershipChange result = default;
		Packer<OwnershipChange>.Read(packer, ref result);

		Assert.AreEqual(1, result.identities.Count);
		Assert.AreEqual(ids[0], result.identities[0]);
		Assert.IsTrue(result.isAdding);

		result.identities.Dispose();
	}

	[Test]
	public void EmptyList_RoundTrips()
	{
		var scope = new PlayerID(6, false);

		using var ids = DisposableList<NetworkID>.Create(0);

		var original = MakeChange(ids, scope);

		packer.ResetPositionAndMode(false);
		Packer<OwnershipChange>.Write(packer, original);

		packer.ResetPositionAndMode(true);
		OwnershipChange result = default;
		Packer<OwnershipChange>.Read(packer, ref result);

		Assert.AreEqual(0, result.identities.Count);

		result.identities.Dispose();
	}
}