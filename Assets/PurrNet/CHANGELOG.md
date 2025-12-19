## [1.18.1-beta.3](https://github.com/PurrNet/PurrNet/compare/v1.18.1-beta.2...v1.18.1-beta.3) (2025-12-19)


### Bug Fixes

* fuck you c# ([d1986df](https://github.com/PurrNet/PurrNet/commit/d1986df12b414c59e1f81861677a7dfffa90b996))

## [1.18.1-beta.2](https://github.com/PurrNet/PurrNet/compare/v1.18.1-beta.1...v1.18.1-beta.2) (2025-12-19)


### Bug Fixes

* dont use static constructor ([0ad1ced](https://github.com/PurrNet/PurrNet/commit/0ad1ced6c34502bcfe91c298b7fe08fb250422ee))

## [1.18.1-beta.1](https://github.com/PurrNet/PurrNet/compare/v1.18.0...v1.18.1-beta.1) (2025-12-19)


### Bug Fixes

* give each thread it's own bit packet pool ([0d5dd6c](https://github.com/PurrNet/PurrNet/commit/0d5dd6c40209cccf593cb880592a2c75a7ae4fb5))

# [1.18.0](https://github.com/PurrNet/PurrNet/compare/v1.17.0...v1.18.0) (2025-12-18)


### Bug Fixes

* Added sync event without data ([4674dfe](https://github.com/PurrNet/PurrNet/commit/4674dfea96fef1cfbf4b9e6618c76564415af52e))
* allow PurrTransport.cs to kick connections ([f1fcc4a](https://github.com/PurrNet/PurrNet/commit/f1fcc4a72c34549dcb86a78f0567124563c4a433))
* allow to override `propagateToChildren` when giving/removing ownership ([8fb5cf7](https://github.com/PurrNet/PurrNet/commit/8fb5cf73b2571e46e022bfd046f72a711f50bfb2))
* Big cleanup of SyncEvent ([633a0ce](https://github.com/PurrNet/PurrNet/commit/633a0cebb936f4398d1d7b37f5802103dae9388b))
* big data bugs and sync texture! ([ca70b79](https://github.com/PurrNet/PurrNet/commit/ca70b79dc55cb42937dcdc2f041c3c9a1ec0e5c2))
* clear connections when SteamTransport starts ([c77f166](https://github.com/PurrNet/PurrNet/commit/c77f16671bc0f488e10df8fca4db1bb96d438a6f))
* clear partial data if owner disconnected mid download ([0bb0cd9](https://github.com/PurrNet/PurrNet/commit/0bb0cd973e0badcaa4ccea88320b9bc44f477408))
* despawn event not called, by the time OnDestroy arrives the owner info is wrong ([d0c090e](https://github.com/PurrNet/PurrNet/commit/d0c090ef72980430fe8c237877d07645a4f3261f))
* don't auto despawn manually spawned identities ([ca52e95](https://github.com/PurrNet/PurrNet/commit/ca52e951c8de5d59c3684392f0e4e83eb5f6fac5))
* double download/update bug for big data ([a785e7f](https://github.com/PurrNet/PurrNet/commit/a785e7f7c5652f0452b552964f0bb614b9a7051f))
* for 6.3+ doesnt make sense to allow for None so lets just ignore it ([7c4bfac](https://github.com/PurrNet/PurrNet/commit/7c4bfacf2313367752120059c3ded04d103f53e2))
* generic RPC bad formated IL ([c090c17](https://github.com/PurrNet/PurrNet/commit/c090c178901f2e8f673d9518f0781a7f6de796c8))
* half quaternion acting up ([9bf5a2c](https://github.com/PurrNet/PurrNet/commit/9bf5a2cdf919eabc8fd64e0ef0906b92d262b19e))
* if server is starting wait for it to fully spin up before starting client ([4bbf0d6](https://github.com/PurrNet/PurrNet/commit/4bbf0d6bc233d7029dec93607991a3ec3850712d))
* make sure big data doesn't mix with old big data (unreliable) ([cabc112](https://github.com/PurrNet/PurrNet/commit/cabc1126c88f47b11659c309448efe6441bd5ef9))
* merging of additions was too aggressive ([77549fe](https://github.com/PurrNet/PurrNet/commit/77549fe8a30873b638a717110dd7d5a6a4ef81ac))
* MTU overflow bug ([2036d21](https://github.com/PurrNet/PurrNet/commit/2036d21b4bfd3c67ece85ba38d958c5508d72d7e))
* network rule to allow target rpcs to target server ([e8339c3](https://github.com/PurrNet/PurrNet/commit/e8339c3b8b301ff4a2520fbb00554afc9752fc85))
* NetworkBones.cs cached ID was not being reset when packet was split ([96dcb34](https://github.com/PurrNet/PurrNet/commit/96dcb34613ea279b65d1a6d9f098aca8c62d9460))
* observer events need to flush RPCs for the onspawned to be processed correctly ([67cf99f](https://github.com/PurrNet/PurrNet/commit/67cf99f2da195a3da29a9d33698b95d85f3361e1))
* obsolete code in unity 6.3+ ([0c404b6](https://github.com/PurrNet/PurrNet/commit/0c404b6e40e736890fde757ce1bbad79048d2a87))
* packer crashing issue due to bad argument handling for method invocation ([a376d3c](https://github.com/PurrNet/PurrNet/commit/a376d3cf5600ea0d1717c42c6b0eb84c3fc7bae5))
* parenting was broken from previous NT rework ([9536c5d](https://github.com/PurrNet/PurrNet/commit/9536c5d180b7e7f22ccce5c7b452128c091434c0))
* Player Identity AOT safety ([520b268](https://github.com/PurrNet/PurrNet/commit/520b2682d214ff97e1a5b922f6fe40ad13ef3a9a))
* pooled array not being cleared broke some functions ([70b8121](https://github.com/PurrNet/PurrNet/commit/70b81214b858a2ac3d4917c1fa3d7eadd9a548f2))
* remove obsolete code ([20c381a](https://github.com/PurrNet/PurrNet/commit/20c381a564b35c93507147a6b42f0ff325341ae1))
* rpc batching and ownership ([bd35c80](https://github.com/PurrNet/PurrNet/commit/bd35c80434a2c1de288fd1a75b504f77ae4e010d))
* send parent change on event instead of delaying it further ([7e03bd6](https://github.com/PurrNet/PurrNet/commit/7e03bd6b03137d38533eb52a646efd0dbbcbb870))
* simplify float delta packing, old packer just added more overhead ([3d2b0f0](https://github.com/PurrNet/PurrNet/commit/3d2b0f0e773621ac2744190a0a898511f722679d))
* some network transform ordering issues ([cb4eed1](https://github.com/PurrNet/PurrNet/commit/cb4eed1355797d0990244447aacb221eba8f20aa))
* spawning concurrency bug ([6af756e](https://github.com/PurrNet/PurrNet/commit/6af756ecaead37b8e73a40326dd591fe055af0c7))
* SyncBigData.cs now supports owner auth and switching ([80a6932](https://github.com/PurrNet/PurrNet/commit/80a693297d4ea51d38f2633a6d4fc1b408d0c266))
* SyncList owner auth fix ([19d044f](https://github.com/PurrNet/PurrNet/commit/19d044f76d1af20cf91da1dcb92cb5ee63e8920d))
* SyncTimer fix from Valentins mistake ([afd5d42](https://github.com/PurrNet/PurrNet/commit/afd5d42ab9897e1fcb87e646e6d8057d198ea461))
* syncvar cleanup and optimisations ([6eae47b](https://github.com/PurrNet/PurrNet/commit/6eae47b1d5b5e25d5b667ec17af77e1a2f05f3d9))
* udp disconnect reason ([0156338](https://github.com/PurrNet/PurrNet/commit/01563389b31084fbd69370e784839cbc92b61fa2))
* unity 6.3 toolbar fixes ([7619b8e](https://github.com/PurrNet/PurrNet/commit/7619b8e462d860258c733d3d83b008dee033369b))
* use HierarchyV2.SetLocalPosAndRot instead of dup logic ([3732cbd](https://github.com/PurrNet/PurrNet/commit/3732cbdeef22c4d295233cee973bbb498584834c))
* when playmode window layout changes it clears the wrapped GUI... ([49b5554](https://github.com/PurrNet/PurrNet/commit/49b555430b7c06c9b026c54f2df617ef3db0e665))


### Features

* add a new rule 'enable host migration' ([7b9b083](https://github.com/PurrNet/PurrNet/commit/7b9b083a94773370ba2cd004a07e349eb2c4d8cd))
* promoting client to server ([36569e4](https://github.com/PurrNet/PurrNet/commit/36569e494030125dccf757572e2debc54e470ca3))
* rpc batching and header delta compression ([639532c](https://github.com/PurrNet/PurrNet/commit/639532c4a790e1b8970c67737a79d54c53f69e58))

# [1.18.0-beta.27](https://github.com/PurrNet/PurrNet/compare/v1.18.0-beta.26...v1.18.0-beta.27) (2025-12-16)


### Bug Fixes

* packer crashing issue due to bad argument handling for method invocation ([a376d3c](https://github.com/PurrNet/PurrNet/commit/a376d3cf5600ea0d1717c42c6b0eb84c3fc7bae5))

# [1.18.0-beta.26](https://github.com/PurrNet/PurrNet/compare/v1.18.0-beta.25...v1.18.0-beta.26) (2025-12-16)


### Bug Fixes

* send parent change on event instead of delaying it further ([7e03bd6](https://github.com/PurrNet/PurrNet/commit/7e03bd6b03137d38533eb52a646efd0dbbcbb870))

# [1.18.0-beta.25](https://github.com/PurrNet/PurrNet/compare/v1.18.0-beta.24...v1.18.0-beta.25) (2025-12-15)


### Bug Fixes

* Added sync event without data ([4674dfe](https://github.com/PurrNet/PurrNet/commit/4674dfea96fef1cfbf4b9e6618c76564415af52e))

# [1.18.0-beta.24](https://github.com/PurrNet/PurrNet/compare/v1.18.0-beta.23...v1.18.0-beta.24) (2025-12-15)


### Bug Fixes

* if server is starting wait for it to fully spin up before starting client ([4bbf0d6](https://github.com/PurrNet/PurrNet/commit/4bbf0d6bc233d7029dec93607991a3ec3850712d))

# [1.18.0-beta.23](https://github.com/PurrNet/PurrNet/compare/v1.18.0-beta.22...v1.18.0-beta.23) (2025-12-15)


### Bug Fixes

* clear connections when SteamTransport starts ([c77f166](https://github.com/PurrNet/PurrNet/commit/c77f16671bc0f488e10df8fca4db1bb96d438a6f))

# [1.18.0-beta.22](https://github.com/PurrNet/PurrNet/compare/v1.18.0-beta.21...v1.18.0-beta.22) (2025-12-14)


### Bug Fixes

* simplify float delta packing, old packer just added more overhead ([3d2b0f0](https://github.com/PurrNet/PurrNet/commit/3d2b0f0e773621ac2744190a0a898511f722679d))

# [1.18.0-beta.21](https://github.com/PurrNet/PurrNet/compare/v1.18.0-beta.20...v1.18.0-beta.21) (2025-12-14)


### Bug Fixes

* NetworkBones.cs cached ID was not being reset when packet was split ([96dcb34](https://github.com/PurrNet/PurrNet/commit/96dcb34613ea279b65d1a6d9f098aca8c62d9460))

# [1.18.0-beta.20](https://github.com/PurrNet/PurrNet/compare/v1.18.0-beta.19...v1.18.0-beta.20) (2025-12-13)


### Bug Fixes

* SyncTimer fix from Valentins mistake ([afd5d42](https://github.com/PurrNet/PurrNet/commit/afd5d42ab9897e1fcb87e646e6d8057d198ea461))

# [1.18.0-beta.19](https://github.com/PurrNet/PurrNet/compare/v1.18.0-beta.18...v1.18.0-beta.19) (2025-12-12)


### Bug Fixes

* allow to override `propagateToChildren` when giving/removing ownership ([8fb5cf7](https://github.com/PurrNet/PurrNet/commit/8fb5cf73b2571e46e022bfd046f72a711f50bfb2))

# [1.18.0-beta.18](https://github.com/PurrNet/PurrNet/compare/v1.18.0-beta.17...v1.18.0-beta.18) (2025-12-11)


### Bug Fixes

* MTU overflow bug ([2036d21](https://github.com/PurrNet/PurrNet/commit/2036d21b4bfd3c67ece85ba38d958c5508d72d7e))

# [1.18.0-beta.17](https://github.com/PurrNet/PurrNet/compare/v1.18.0-beta.16...v1.18.0-beta.17) (2025-12-09)


### Bug Fixes

* don't auto despawn manually spawned identities ([ca52e95](https://github.com/PurrNet/PurrNet/commit/ca52e951c8de5d59c3684392f0e4e83eb5f6fac5))
* for 6.3+ doesnt make sense to allow for None so lets just ignore it ([7c4bfac](https://github.com/PurrNet/PurrNet/commit/7c4bfacf2313367752120059c3ded04d103f53e2))
* obsolete code in unity 6.3+ ([0c404b6](https://github.com/PurrNet/PurrNet/commit/0c404b6e40e736890fde757ce1bbad79048d2a87))
* udp disconnect reason ([0156338](https://github.com/PurrNet/PurrNet/commit/01563389b31084fbd69370e784839cbc92b61fa2))
* unity 6.3 toolbar fixes ([7619b8e](https://github.com/PurrNet/PurrNet/commit/7619b8e462d860258c733d3d83b008dee033369b))
* when playmode window layout changes it clears the wrapped GUI... ([49b5554](https://github.com/PurrNet/PurrNet/commit/49b555430b7c06c9b026c54f2df617ef3db0e665))


### Features

* add a new rule 'enable host migration' ([7b9b083](https://github.com/PurrNet/PurrNet/commit/7b9b083a94773370ba2cd004a07e349eb2c4d8cd))
* promoting client to server ([36569e4](https://github.com/PurrNet/PurrNet/commit/36569e494030125dccf757572e2debc54e470ca3))

# [1.18.0-beta.16](https://github.com/PurrNet/PurrNet/compare/v1.18.0-beta.15...v1.18.0-beta.16) (2025-12-09)


### Bug Fixes

* Big cleanup of SyncEvent ([633a0ce](https://github.com/PurrNet/PurrNet/commit/633a0cebb936f4398d1d7b37f5802103dae9388b))

# [1.18.0-beta.15](https://github.com/PurrNet/PurrNet/compare/v1.18.0-beta.14...v1.18.0-beta.15) (2025-12-07)


### Bug Fixes

* allow PurrTransport.cs to kick connections ([f1fcc4a](https://github.com/PurrNet/PurrNet/commit/f1fcc4a72c34549dcb86a78f0567124563c4a433))

# [1.18.0-beta.14](https://github.com/PurrNet/PurrNet/compare/v1.18.0-beta.13...v1.18.0-beta.14) (2025-12-05)


### Bug Fixes

* despawn event not called, by the time OnDestroy arrives the owner info is wrong ([d0c090e](https://github.com/PurrNet/PurrNet/commit/d0c090ef72980430fe8c237877d07645a4f3261f))

# [1.18.0-beta.13](https://github.com/PurrNet/PurrNet/compare/v1.18.0-beta.12...v1.18.0-beta.13) (2025-12-05)


### Bug Fixes

* observer events need to flush RPCs for the onspawned to be processed correctly ([67cf99f](https://github.com/PurrNet/PurrNet/commit/67cf99f2da195a3da29a9d33698b95d85f3361e1))

# [1.18.0-beta.12](https://github.com/PurrNet/PurrNet/compare/v1.18.0-beta.11...v1.18.0-beta.12) (2025-12-04)


### Bug Fixes

* use HierarchyV2.SetLocalPosAndRot instead of dup logic ([3732cbd](https://github.com/PurrNet/PurrNet/commit/3732cbdeef22c4d295233cee973bbb498584834c))

# [1.18.0-beta.11](https://github.com/PurrNet/PurrNet/compare/v1.18.0-beta.10...v1.18.0-beta.11) (2025-12-04)


### Bug Fixes

* Player Identity AOT safety ([520b268](https://github.com/PurrNet/PurrNet/commit/520b2682d214ff97e1a5b922f6fe40ad13ef3a9a))

# [1.18.0-beta.10](https://github.com/PurrNet/PurrNet/compare/v1.18.0-beta.9...v1.18.0-beta.10) (2025-12-03)


### Bug Fixes

* generic RPC bad formated IL ([c090c17](https://github.com/PurrNet/PurrNet/commit/c090c178901f2e8f673d9518f0781a7f6de796c8))

# [1.18.0-beta.9](https://github.com/PurrNet/PurrNet/compare/v1.18.0-beta.8...v1.18.0-beta.9) (2025-12-02)


### Bug Fixes

* syncvar cleanup and optimisations ([6eae47b](https://github.com/PurrNet/PurrNet/commit/6eae47b1d5b5e25d5b667ec17af77e1a2f05f3d9))

# [1.18.0-beta.8](https://github.com/PurrNet/PurrNet/compare/v1.18.0-beta.7...v1.18.0-beta.8) (2025-11-28)


### Bug Fixes

* SyncList owner auth fix ([19d044f](https://github.com/PurrNet/PurrNet/commit/19d044f76d1af20cf91da1dcb92cb5ee63e8920d))

# [1.18.0-beta.7](https://github.com/PurrNet/PurrNet/compare/v1.18.0-beta.6...v1.18.0-beta.7) (2025-11-27)


### Bug Fixes

* clear partial data if owner disconnected mid download ([0bb0cd9](https://github.com/PurrNet/PurrNet/commit/0bb0cd973e0badcaa4ccea88320b9bc44f477408))

# [1.18.0-beta.6](https://github.com/PurrNet/PurrNet/compare/v1.18.0-beta.5...v1.18.0-beta.6) (2025-11-24)


### Bug Fixes

* merging of additions was too aggressive ([77549fe](https://github.com/PurrNet/PurrNet/commit/77549fe8a30873b638a717110dd7d5a6a4ef81ac))

# [1.18.0-beta.5](https://github.com/PurrNet/PurrNet/compare/v1.18.0-beta.4...v1.18.0-beta.5) (2025-11-24)


### Bug Fixes

* half quaternion acting up ([9bf5a2c](https://github.com/PurrNet/PurrNet/commit/9bf5a2cdf919eabc8fd64e0ef0906b92d262b19e))

# [1.18.0-beta.4](https://github.com/PurrNet/PurrNet/compare/v1.18.0-beta.3...v1.18.0-beta.4) (2025-11-24)


### Bug Fixes

* rpc batching and ownership ([bd35c80](https://github.com/PurrNet/PurrNet/commit/bd35c80434a2c1de288fd1a75b504f77ae4e010d))

# [1.18.0-beta.3](https://github.com/PurrNet/PurrNet/compare/v1.18.0-beta.2...v1.18.0-beta.3) (2025-11-23)


### Bug Fixes

* some network transform ordering issues ([cb4eed1](https://github.com/PurrNet/PurrNet/commit/cb4eed1355797d0990244447aacb221eba8f20aa))

# [1.18.0-beta.2](https://github.com/PurrNet/PurrNet/compare/v1.18.0-beta.1...v1.18.0-beta.2) (2025-11-23)


### Bug Fixes

* pooled array not being cleared broke some functions ([70b8121](https://github.com/PurrNet/PurrNet/commit/70b81214b858a2ac3d4917c1fa3d7eadd9a548f2))

# [1.18.0-beta.1](https://github.com/PurrNet/PurrNet/compare/v1.17.1-beta.8...v1.18.0-beta.1) (2025-11-21)


### Features

* rpc batching and header delta compression ([639532c](https://github.com/PurrNet/PurrNet/commit/639532c4a790e1b8970c67737a79d54c53f69e58))

## [1.17.1-beta.8](https://github.com/PurrNet/PurrNet/compare/v1.17.1-beta.7...v1.17.1-beta.8) (2025-11-20)


### Bug Fixes

* double download/update bug for big data ([a785e7f](https://github.com/PurrNet/PurrNet/commit/a785e7f7c5652f0452b552964f0bb614b9a7051f))

## [1.17.1-beta.7](https://github.com/PurrNet/PurrNet/compare/v1.17.1-beta.6...v1.17.1-beta.7) (2025-11-19)


### Bug Fixes

* big data bugs and sync texture! ([ca70b79](https://github.com/PurrNet/PurrNet/commit/ca70b79dc55cb42937dcdc2f041c3c9a1ec0e5c2))

## [1.17.1-beta.6](https://github.com/PurrNet/PurrNet/compare/v1.17.1-beta.5...v1.17.1-beta.6) (2025-11-19)


### Bug Fixes

* remove obsolete code ([20c381a](https://github.com/PurrNet/PurrNet/commit/20c381a564b35c93507147a6b42f0ff325341ae1))

## [1.17.1-beta.5](https://github.com/PurrNet/PurrNet/compare/v1.17.1-beta.4...v1.17.1-beta.5) (2025-11-19)


### Bug Fixes

* spawning concurrency bug ([6af756e](https://github.com/PurrNet/PurrNet/commit/6af756ecaead37b8e73a40326dd591fe055af0c7))

## [1.17.1-beta.4](https://github.com/PurrNet/PurrNet/compare/v1.17.1-beta.3...v1.17.1-beta.4) (2025-11-19)


### Bug Fixes

* parenting was broken from previous NT rework ([9536c5d](https://github.com/PurrNet/PurrNet/commit/9536c5d180b7e7f22ccce5c7b452128c091434c0))

## [1.17.1-beta.3](https://github.com/PurrNet/PurrNet/compare/v1.17.1-beta.2...v1.17.1-beta.3) (2025-11-19)


### Bug Fixes

* make sure big data doesn't mix with old big data (unreliable) ([cabc112](https://github.com/PurrNet/PurrNet/commit/cabc1126c88f47b11659c309448efe6441bd5ef9))

## [1.17.1-beta.2](https://github.com/PurrNet/PurrNet/compare/v1.17.1-beta.1...v1.17.1-beta.2) (2025-11-19)


### Bug Fixes

* SyncBigData.cs now supports owner auth and switching ([80a6932](https://github.com/PurrNet/PurrNet/commit/80a693297d4ea51d38f2633a6d4fc1b408d0c266))

## [1.17.1-beta.1](https://github.com/PurrNet/PurrNet/compare/v1.17.0...v1.17.1-beta.1) (2025-11-19)


### Bug Fixes

* network rule to allow target rpcs to target server ([e8339c3](https://github.com/PurrNet/PurrNet/commit/e8339c3b8b301ff4a2520fbb00554afc9752fc85))

# [1.17.0](https://github.com/PurrNet/PurrNet/compare/v1.16.0...v1.17.0) (2025-11-18)


### Bug Fixes

* actually register disposableArray ([c0f7b0f](https://github.com/PurrNet/PurrNet/commit/c0f7b0f85213304459111acbaa607dd834dbf7e1))
* Added action subscription to syncevent ([e30ed2a](https://github.com/PurrNet/PurrNet/commit/e30ed2a40f406fe833f5453cbdba4bfdf4fa32ec))
* Added summary for new sync timer advancing ([0b5be52](https://github.com/PurrNet/PurrNet/commit/0b5be5285c2458e1351da220a6c1088699514d2f))
* allow to manually clear all subscriptions from network manaher using `ResetInternalState` ([f24f0a8](https://github.com/PurrNet/PurrNet/commit/f24f0a897d64b19a7d7108f6634b87ed2d844e3e))
* allow to override how things are duplicated ([c99ba8f](https://github.com/PurrNet/PurrNet/commit/c99ba8f122f6b10bf1bf7f335d1b1df623ec7781))
* bad symbol name ([0d94952](https://github.com/PurrNet/PurrNet/commit/0d949526be21c408ab0a979965fffde58b2a2ac2))
* building errors ([d0ce345](https://github.com/PurrNet/PurrNet/commit/d0ce34535f668d9bad6759385d3bf3c455d12c83))
* clear static stuff for PlayerIdentity.cs ([da22af9](https://github.com/PurrNet/PurrNet/commit/da22af9c0d1b433e700130484622259dd763ca33))
* compiler error due to bad #if ([41cc6c6](https://github.com/PurrNet/PurrNet/commit/41cc6c62928d1f9dd037751e9f44b729b82399c8))
* error when adding component at runtime ([a7d068c](https://github.com/PurrNet/PurrNet/commit/a7d068c81001e645d49921f9caced42c7eef7840))
* forwarding data with delta packer ([135e8df](https://github.com/PurrNet/PurrNet/commit/135e8df2f9147fd2100d1edc71c87d13bb45c265))
* IDuplicates of the disposable collections were wrong for managed types ([a11a9be](https://github.com/PurrNet/PurrNet/commit/a11a9be2ca2a249328a21b4cc0376fcd501a7d5e))
* if collection is disposed just return default value ([80cc7a9](https://github.com/PurrNet/PurrNet/commit/80cc7a9e0434d482ee6630641eb00c6a9445db2e))
* Improved validated syncvar handling ([4fe4434](https://github.com/PurrNet/PurrNet/commit/4fe443447d15264ff6f110fa4e12978b347ad020))
* make ownership clearer for InterlatedWithDispose ([2615cc7](https://github.com/PurrNet/PurrNet/commit/2615cc7944bbed4d4c13ec25f2e77cc480ccc3bb))
* make sure `OnTick` exceptions don't have side effects to other subscribers ([8811429](https://github.com/PurrNet/PurrNet/commit/88114295949b5b64936c0becb7d5854f2714a744))
* order of add was screwed by my previous attempt to be smart ([315dd96](https://github.com/PurrNet/PurrNet/commit/315dd966ef007c8e08e355aeefb58f6e77415ba5))
* PlayerIdentity<T> catchup in OnSpawned when it happens at a later stage ([53305a4](https://github.com/PurrNet/PurrNet/commit/53305a468694272e151bb1bbc7e6901cc8bb3f02))
* previous undo compiler errors ([a889ac8](https://github.com/PurrNet/PurrNet/commit/a889ac894ce77a344491395b154c604e54beb1ef))
* profiler/statistics locked to editor only ([d8272f7](https://github.com/PurrNet/PurrNet/commit/d8272f747871778d69a68de5ea7943dc93769121))
* properly filter players when forwarding rpcs ([3b05719](https://github.com/PurrNet/PurrNet/commit/3b057195282236c496337875544d938946ce4731))
* properly register interfaces and collections of interfaces ([dd0b28e](https://github.com/PurrNet/PurrNet/commit/dd0b28edb6f5c0ea06415eba4c9ee1ed577764a5))
* PURR_LEAKS_CHECK for dictionaries too ([3c1b8d6](https://github.com/PurrNet/PurrNet/commit/3c1b8d652685969fe82f5a2341a6073d75ac3414))
* scene pooling not clearing properly ([1acdd22](https://github.com/PurrNet/PurrNet/commit/1acdd222acf5fc86b1ac3ef7b01d507f3b9137e7))
* State machine host fix ([15bfbcd](https://github.com/PurrNet/PurrNet/commit/15bfbcd1a912ad2a9bceb50dbbb3d8468005595d))
* static delta rpcs ([77ce2b4](https://github.com/PurrNet/PurrNet/commit/77ce2b4815eb474ef4bb308799c0d3cfafe4209f))
* syncvar events should be triggered when Packer.Transform is successful ([abbc918](https://github.com/PurrNet/PurrNet/commit/abbc918f033be615831efdcc2aa61835707c71d7))
* try to match static rpc behaviour to normal rpcs ([267f5c5](https://github.com/PurrNet/PurrNet/commit/267f5c57e0d5d9efe6908a0d6ec1284c565d84d2))
* trying to make DDOL deterministic ([d03647c](https://github.com/PurrNet/PurrNet/commit/d03647c71602ef588c165bf86854be9bf7f5efad))
* useDeltaPacking for rpcs, still WIP ([1611074](https://github.com/PurrNet/PurrNet/commit/16110740d3f9ae7829b70b90880ebc2aa4947b91))
* work around c# methods that generate GC ([59d660f](https://github.com/PurrNet/PurrNet/commit/59d660fcda78f1fe5543350ad28c6035accba61c))


### Features

* Added Validated Syncvar ([49f9343](https://github.com/PurrNet/PurrNet/commit/49f9343b86e10e9f8d1b1db5c5f4c878aa19eb2f))

# [1.17.0-beta.22](https://github.com/PurrNet/PurrNet/compare/v1.17.0-beta.21...v1.17.0-beta.22) (2025-11-13)


### Bug Fixes

* Added action subscription to syncevent ([e30ed2a](https://github.com/PurrNet/PurrNet/commit/e30ed2a40f406fe833f5453cbdba4bfdf4fa32ec))

# [1.17.0-beta.21](https://github.com/PurrNet/PurrNet/compare/v1.17.0-beta.20...v1.17.0-beta.21) (2025-11-13)


### Bug Fixes

* order of add was screwed by my previous attempt to be smart ([315dd96](https://github.com/PurrNet/PurrNet/commit/315dd966ef007c8e08e355aeefb58f6e77415ba5))

# [1.17.0-beta.20](https://github.com/PurrNet/PurrNet/compare/v1.17.0-beta.19...v1.17.0-beta.20) (2025-11-13)


### Bug Fixes

* building errors ([d0ce345](https://github.com/PurrNet/PurrNet/commit/d0ce34535f668d9bad6759385d3bf3c455d12c83))

# [1.17.0-beta.19](https://github.com/PurrNet/PurrNet/compare/v1.17.0-beta.18...v1.17.0-beta.19) (2025-11-13)


### Bug Fixes

* if collection is disposed just return default value ([80cc7a9](https://github.com/PurrNet/PurrNet/commit/80cc7a9e0434d482ee6630641eb00c6a9445db2e))

# [1.17.0-beta.18](https://github.com/PurrNet/PurrNet/compare/v1.17.0-beta.17...v1.17.0-beta.18) (2025-11-13)


### Bug Fixes

* work around c# methods that generate GC ([59d660f](https://github.com/PurrNet/PurrNet/commit/59d660fcda78f1fe5543350ad28c6035accba61c))

# [1.17.0-beta.17](https://github.com/PurrNet/PurrNet/compare/v1.17.0-beta.16...v1.17.0-beta.17) (2025-11-13)


### Bug Fixes

* IDuplicates of the disposable collections were wrong for managed types ([a11a9be](https://github.com/PurrNet/PurrNet/commit/a11a9be2ca2a249328a21b4cc0376fcd501a7d5e))

# [1.17.0-beta.16](https://github.com/PurrNet/PurrNet/compare/v1.17.0-beta.15...v1.17.0-beta.16) (2025-11-13)


### Bug Fixes

* make ownership clearer for InterlatedWithDispose ([2615cc7](https://github.com/PurrNet/PurrNet/commit/2615cc7944bbed4d4c13ec25f2e77cc480ccc3bb))

# [1.17.0-beta.15](https://github.com/PurrNet/PurrNet/compare/v1.17.0-beta.14...v1.17.0-beta.15) (2025-11-12)


### Bug Fixes

* make sure `OnTick` exceptions don't have side effects to other subscribers ([8811429](https://github.com/PurrNet/PurrNet/commit/88114295949b5b64936c0becb7d5854f2714a744))

# [1.17.0-beta.14](https://github.com/PurrNet/PurrNet/compare/v1.17.0-beta.13...v1.17.0-beta.14) (2025-11-12)


### Bug Fixes

* scene pooling not clearing properly ([1acdd22](https://github.com/PurrNet/PurrNet/commit/1acdd222acf5fc86b1ac3ef7b01d507f3b9137e7))

# [1.17.0-beta.13](https://github.com/PurrNet/PurrNet/compare/v1.17.0-beta.12...v1.17.0-beta.13) (2025-11-11)


### Bug Fixes

* trying to make DDOL deterministic ([d03647c](https://github.com/PurrNet/PurrNet/commit/d03647c71602ef588c165bf86854be9bf7f5efad))

# [1.17.0-beta.12](https://github.com/PurrNet/PurrNet/compare/v1.17.0-beta.11...v1.17.0-beta.12) (2025-11-11)


### Bug Fixes

* properly filter players when forwarding rpcs ([3b05719](https://github.com/PurrNet/PurrNet/commit/3b057195282236c496337875544d938946ce4731))

# [1.17.0-beta.11](https://github.com/PurrNet/PurrNet/compare/v1.17.0-beta.10...v1.17.0-beta.11) (2025-11-11)


### Bug Fixes

* static delta rpcs ([77ce2b4](https://github.com/PurrNet/PurrNet/commit/77ce2b4815eb474ef4bb308799c0d3cfafe4209f))
* syncvar events should be triggered when Packer.Transform is successful ([abbc918](https://github.com/PurrNet/PurrNet/commit/abbc918f033be615831efdcc2aa61835707c71d7))

# [1.17.0-beta.10](https://github.com/PurrNet/PurrNet/compare/v1.17.0-beta.9...v1.17.0-beta.10) (2025-11-11)


### Bug Fixes

* compiler error due to bad #if ([41cc6c6](https://github.com/PurrNet/PurrNet/commit/41cc6c62928d1f9dd037751e9f44b729b82399c8))

# [1.17.0-beta.9](https://github.com/PurrNet/PurrNet/compare/v1.17.0-beta.8...v1.17.0-beta.9) (2025-11-10)


### Bug Fixes

* allow to manually clear all subscriptions from network manaher using `ResetInternalState` ([f24f0a8](https://github.com/PurrNet/PurrNet/commit/f24f0a897d64b19a7d7108f6634b87ed2d844e3e))

# [1.17.0-beta.8](https://github.com/PurrNet/PurrNet/compare/v1.17.0-beta.7...v1.17.0-beta.8) (2025-11-10)


### Bug Fixes

* previous undo compiler errors ([a889ac8](https://github.com/PurrNet/PurrNet/commit/a889ac894ce77a344491395b154c604e54beb1ef))

# [1.17.0-beta.7](https://github.com/PurrNet/PurrNet/compare/v1.17.0-beta.6...v1.17.0-beta.7) (2025-11-07)


### Bug Fixes

* forwarding data with delta packer ([135e8df](https://github.com/PurrNet/PurrNet/commit/135e8df2f9147fd2100d1edc71c87d13bb45c265))

# [1.17.0-beta.6](https://github.com/PurrNet/PurrNet/compare/v1.17.0-beta.5...v1.17.0-beta.6) (2025-11-06)


### Bug Fixes

* useDeltaPacking for rpcs, still WIP ([1611074](https://github.com/PurrNet/PurrNet/commit/16110740d3f9ae7829b70b90880ebc2aa4947b91))

# [1.17.0-beta.5](https://github.com/PurrNet/PurrNet/compare/v1.17.0-beta.4...v1.17.0-beta.5) (2025-11-04)


### Bug Fixes

* clear static stuff for PlayerIdentity.cs ([da22af9](https://github.com/PurrNet/PurrNet/commit/da22af9c0d1b433e700130484622259dd763ca33))

# [1.17.0-beta.4](https://github.com/PurrNet/PurrNet/compare/v1.17.0-beta.3...v1.17.0-beta.4) (2025-11-04)


### Bug Fixes

* try to match static rpc behaviour to normal rpcs ([267f5c5](https://github.com/PurrNet/PurrNet/commit/267f5c57e0d5d9efe6908a0d6ec1284c565d84d2))

# [1.17.0-beta.3](https://github.com/PurrNet/PurrNet/compare/v1.17.0-beta.2...v1.17.0-beta.3) (2025-11-04)


### Bug Fixes

* PlayerIdentity<T> catchup in OnSpawned when it happens at a later stage ([53305a4](https://github.com/PurrNet/PurrNet/commit/53305a468694272e151bb1bbc7e6901cc8bb3f02))

# [1.17.0-beta.2](https://github.com/PurrNet/PurrNet/compare/v1.17.0-beta.1...v1.17.0-beta.2) (2025-11-03)


### Bug Fixes

* Improved validated syncvar handling ([4fe4434](https://github.com/PurrNet/PurrNet/commit/4fe443447d15264ff6f110fa4e12978b347ad020))

# [1.17.0-beta.1](https://github.com/PurrNet/PurrNet/compare/v1.16.1-beta.8...v1.17.0-beta.1) (2025-11-03)


### Features

* Added Validated Syncvar ([49f9343](https://github.com/PurrNet/PurrNet/commit/49f9343b86e10e9f8d1b1db5c5f4c878aa19eb2f))

## [1.16.1-beta.8](https://github.com/PurrNet/PurrNet/compare/v1.16.1-beta.7...v1.16.1-beta.8) (2025-11-03)


### Bug Fixes

* Added summary for new sync timer advancing ([0b5be52](https://github.com/PurrNet/PurrNet/commit/0b5be5285c2458e1351da220a6c1088699514d2f))

## [1.16.1-beta.7](https://github.com/PurrNet/PurrNet/compare/v1.16.1-beta.6...v1.16.1-beta.7) (2025-11-02)


### Bug Fixes

* properly register interfaces and collections of interfaces ([dd0b28e](https://github.com/PurrNet/PurrNet/commit/dd0b28edb6f5c0ea06415eba4c9ee1ed577764a5))

## [1.16.1-beta.6](https://github.com/PurrNet/PurrNet/compare/v1.16.1-beta.5...v1.16.1-beta.6) (2025-10-28)


### Bug Fixes

* allow to override how things are duplicated ([c99ba8f](https://github.com/PurrNet/PurrNet/commit/c99ba8f122f6b10bf1bf7f335d1b1df623ec7781))

## [1.16.1-beta.5](https://github.com/PurrNet/PurrNet/compare/v1.16.1-beta.4...v1.16.1-beta.5) (2025-10-27)


### Bug Fixes

* PURR_LEAKS_CHECK for dictionaries too ([3c1b8d6](https://github.com/PurrNet/PurrNet/commit/3c1b8d652685969fe82f5a2341a6073d75ac3414))

## [1.16.1-beta.4](https://github.com/PurrNet/PurrNet/compare/v1.16.1-beta.3...v1.16.1-beta.4) (2025-10-26)


### Bug Fixes

* error when adding component at runtime ([a7d068c](https://github.com/PurrNet/PurrNet/commit/a7d068c81001e645d49921f9caced42c7eef7840))

## [1.16.1-beta.3](https://github.com/PurrNet/PurrNet/compare/v1.16.1-beta.2...v1.16.1-beta.3) (2025-10-24)


### Bug Fixes

* bad symbol name ([0d94952](https://github.com/PurrNet/PurrNet/commit/0d949526be21c408ab0a979965fffde58b2a2ac2))
* profiler/statistics locked to editor only ([d8272f7](https://github.com/PurrNet/PurrNet/commit/d8272f747871778d69a68de5ea7943dc93769121))

## [1.16.1-beta.2](https://github.com/PurrNet/PurrNet/compare/v1.16.1-beta.1...v1.16.1-beta.2) (2025-10-23)


### Bug Fixes

* actually register disposableArray ([c0f7b0f](https://github.com/PurrNet/PurrNet/commit/c0f7b0f85213304459111acbaa607dd834dbf7e1))

## [1.16.1-beta.1](https://github.com/PurrNet/PurrNet/compare/v1.16.0...v1.16.1-beta.1) (2025-10-22)


### Bug Fixes

* State machine host fix ([15bfbcd](https://github.com/PurrNet/PurrNet/commit/15bfbcd1a912ad2a9bceb50dbbb3d8468005595d))

# [1.16.0](https://github.com/PurrNet/PurrNet/compare/v1.15.0...v1.16.0) (2025-10-17)


### Bug Fixes

* +disposable array ([4c92504](https://github.com/PurrNet/PurrNet/commit/4c925042940abf14b839d1b5b6a7063a56be34b2))
* add `HierarchyV2.onPreSpawn` static event ([0c02749](https://github.com/PurrNet/PurrNet/commit/0c0274922d2cfb1db576c4bb4fbfa4d1e73f50f6))
* add debugging scripting symbol for delta compression that packs extra data to let you know of issues ([2c96204](https://github.com/PurrNet/PurrNet/commit/2c9620425c2ae43e545bbc04ed94205f02e75d6e))
* Added git checking to addon library ([336e590](https://github.com/PurrNet/PurrNet/commit/336e59027c71b3ed96a6dc6d8f4f5a699a6af213))
* Added PurrChat link to toolbar ([72d3d58](https://github.com/PurrNet/PurrNet/commit/72d3d58cf6be44a925837f736c49b7a1af92e93b))
* Added statistics manager display target options ([75d095f](https://github.com/PurrNet/PurrNet/commit/75d095ff1881ed47d836c9af63baf21c90bc09e9))
* allow network animator to reconcile time ([caa62bc](https://github.com/PurrNet/PurrNet/commit/caa62bc81078a38cf5381dad5f888e6186ee3089))
* allow to prioritize non generated packers more explicitly ([0081fbc](https://github.com/PurrNet/PurrNet/commit/0081fbc4d9a25432ce31ac9807dea7ea0a151ab4))
* allow to set extra bones for the NetworkBones component ([7e7872d](https://github.com/PurrNet/PurrNet/commit/7e7872d60c733c7d135a43a95e7c3d6f0a4b70d3))
* attempting to make networktransform more performant ([6a065f2](https://github.com/PurrNet/PurrNet/commit/6a065f210f1d2a6ad238c6a057c512d53fa93b66))
* better error for unitask ([60e6ea2](https://github.com/PurrNet/PurrNet/commit/60e6ea222bc636c3f155d6c9329d0c62ba4cd069))
* cleanup issues ([36abd59](https://github.com/PurrNet/PurrNet/commit/36abd590f60caa6e79d7788e4871805b1014ab0e))
* compiler error for network module due to rework ([2a5a3e5](https://github.com/PurrNet/PurrNet/commit/2a5a3e5701011880acb580f392b2fdbbba673a88))
* compressed float equality failed, stick to storing raw value instead of float value ([2d04a76](https://github.com/PurrNet/PurrNet/commit/2d04a76d83fedd6a2c9f93e10d6c23338d92cb12))
* delta packing registration was faulty ([3db16b8](https://github.com/PurrNet/PurrNet/commit/3db16b844ee0e4379debed08f50f652506235586))
* disposable Array/Hashset collections missing delta packers ([71b1059](https://github.com/PurrNet/PurrNet/commit/71b1059df8a2b1ddc37a31f55128c0fd27d86019))
* disposable dictionary serialization fixes ([0a93474](https://github.com/PurrNet/PurrNet/commit/0a934746bda14c997c2aaaf3948fee32c81f8fd0))
* disposable list myers action application ([e5c3160](https://github.com/PurrNet/PurrNet/commit/e5c3160641a9bea983eb6ef5548359196066e86a))
* don't send irrelevant data for the NT ([315c331](https://github.com/PurrNet/PurrNet/commit/315c33131f45e9faa94e5047814438290f561869))
* GC when validating rpcs ([adeca8a](https://github.com/PurrNet/PurrNet/commit/adeca8a34f869cb39e1cd7700ecc2bbee22cb438))
* handle isServer scenario differently ([d7a930e](https://github.com/PurrNet/PurrNet/commit/d7a930e60060ecf1dbbc56159731420c4edcc047))
* hashset Create and obsolete constructor ([5c35273](https://github.com/PurrNet/PurrNet/commit/5c35273b030333786f95358601fc68cda7609520))
* host disconnecting was despawning other player owned objects due to bad cache ([52e1717](https://github.com/PurrNet/PurrNet/commit/52e171765f8e2b3834ed9ea3178979bbdabfcce4))
* if empty list ([d5c77f3](https://github.com/PurrNet/PurrNet/commit/d5c77f3a2d4dcabc145bdb6ef7ca8333ca99ec40))
* improved some packing for the NT ([e7eab45](https://github.com/PurrNet/PurrNet/commit/e7eab45db557b28cbe57e0f245809173f8697f6d))
* include local pos for child pieces ([4c67434](https://github.com/PurrNet/PurrNet/commit/4c67434b0fa854943a8ca84073931457b83f639c))
* include sceneid for spawn point provider ([2c64600](https://github.com/PurrNet/PurrNet/commit/2c646000874c61432005038c39ed9fe13543e7cd))
* Linked network prefabs logic added ([ba4a2e4](https://github.com/PurrNet/PurrNet/commit/ba4a2e4c4fa2e60e0d86ed7d8e6f3609fded1662))
* make sure any exceptions in the callbacks for synctypes dont break any flow AND that reset pool resets it's internal state ([de2a64c](https://github.com/PurrNet/PurrNet/commit/de2a64c216207ee4b495edab85fd253e3e3634f1))
* make sure disposable list is registered for dictionary ([34e461a](https://github.com/PurrNet/PurrNet/commit/34e461a6adea9617d29f41c740bda036a84b677e))
* make sure scene is valid when unloading ([0d7e8a3](https://github.com/PurrNet/PurrNet/commit/0d7e8a324237d8eec7faa8c7f3c0c0a743ff0553))
* make sure the packer has the proper data when communicating to others ([dc9f03c](https://github.com/PurrNet/PurrNet/commit/dc9f03cd39b184cd261016f187bd72a872a0e44e))
* messaging issue ([2a8c0cb](https://github.com/PurrNet/PurrNet/commit/2a8c0cbf684636d617f85a4eae141e0f0e52b305))
* more optimizations ([7e777de](https://github.com/PurrNet/PurrNet/commit/7e777ded30420e69a00fb6f7a020d6a3893baa0a))
* myers impl ([61b1929](https://github.com/PurrNet/PurrNet/commit/61b19299f17a9d48377a7ad73791c039a63bda45))
* naive delta packer for array and list ([4904eaa](https://github.com/PurrNet/PurrNet/commit/4904eaa69a2b5540b4d81b1a3fdf6000806a77f3))
* network transform module bug ([b6a0a5d](https://github.com/PurrNet/PurrNet/commit/b6a0a5d747f0369d9316123b8619e376f97572e8))
* NetworkTransform `ForceSync` was weird ([c2593fd](https://github.com/PurrNet/PurrNet/commit/c2593fdb3b3b8a019f851302df1016a711386a9f))
* new myers deltalist packer ([f8c6d05](https://github.com/PurrNet/PurrNet/commit/f8c6d058e8c192e3355fe7d768a2ef32825e0ca9))
* only adapt outside of editor ([cacc0c7](https://github.com/PurrNet/PurrNet/commit/cacc0c7bc9d501ee7bbbf65d1e1a028145182612))
* packer was rounding floats for CompressedFloat when it doesn't have to ([e968119](https://github.com/PurrNet/PurrNet/commit/e9681190fd2e6dcb1b91378757d8e6ce548d1942))
* packing for DisposableArray.cs ([1e5b4d2](https://github.com/PurrNet/PurrNet/commit/1e5b4d20907f09d45c03459c7396f8005dd12c7c))
* PurrTransport cache made changing master server a pain ([f642aff](https://github.com/PurrNet/PurrNet/commit/f642aff74b635176dcb1036b2a54f5909f42874b))
* purrtransport compiler error ([62713c5](https://github.com/PurrNet/PurrNet/commit/62713c51f0b792fc762a91725cdc469d00924c75))
* reflection getmethod failing ([568db6c](https://github.com/PurrNet/PurrNet/commit/568db6c39656b5aa315756127740db3c3b1a9f95))
* resolve hostname for the udp transport ([cc86356](https://github.com/PurrNet/PurrNet/commit/cc86356a681413e26bab57564c49d45dd6a8808d))
* set position after parenting ([f7d4dbf](https://github.com/PurrNet/PurrNet/commit/f7d4dbfdc7fe8a17f8dd9a15cf2a5393f0cac25a))
* some delta packing for spawn packet batches ([20388f8](https://github.com/PurrNet/PurrNet/commit/20388f8ef8d4c38c66d3a8fd09e008a4647a1499))
* some host issues with visibility rules ([29476a0](https://github.com/PurrNet/PurrNet/commit/29476a08b5626e1e5b84cb69c6498ab52af084db))
* some more `try catch` and reset whitelist/blacklist state when pool reset called ([5d4958b](https://github.com/PurrNet/PurrNet/commit/5d4958b1bfe948c7c77431740ce15912c92a1a08))
* some packing bugs ([fc9899a](https://github.com/PurrNet/PurrNet/commit/fc9899a1fc8fe9aad5e24aca35b5c429346572b9))
* some packing issues ([f43ee8a](https://github.com/PurrNet/PurrNet/commit/f43ee8a8220c908fda10f94bad6d78ed9c0967ca))
* sorting was backwards ([880a670](https://github.com/PurrNet/PurrNet/commit/880a67069036afed16757245dc6fdf2a58b336d8))
* spawn point provider pattern ([7e20abe](https://github.com/PurrNet/PurrNet/commit/7e20abe1a733511572bb231044dcc4985f38027f))
* steam errors when trying to use connection after closed ([ee4ed6a](https://github.com/PurrNet/PurrNet/commit/ee4ed6ab6c540d85aaf51ed0f5af29d43508e3ce))
* SyncTimer issues ([4d8e7f4](https://github.com/PurrNet/PurrNet/commit/4d8e7f4fc00fb9429eecb74727f73206d0d1350b))
* ulong packing ([c48f735](https://github.com/PurrNet/PurrNet/commit/c48f7354620cf346eba2ffa95ed529b93fb99517))
* unit tests circucal reference issue ([c9c0624](https://github.com/PurrNet/PurrNet/commit/c9c0624dc3715c6ed3f95b957b05c79046344fb2))
* unity version issues ([f8c90e2](https://github.com/PurrNet/PurrNet/commit/f8c90e2ddcd22c1a2a0dc94c427b9041619d1205))


### Features

* Added PlayerIdentity ([93ffe55](https://github.com/PurrNet/PurrNet/commit/93ffe558807b310e344348986d8aab4755893633))
* IStandaloneSerializable ([27e1733](https://github.com/PurrNet/PurrNet/commit/27e17337811855f0b0e5d486416bb1713cffe333))
* purrtransport udp support ([1a0ad4a](https://github.com/PurrNet/PurrNet/commit/1a0ad4ada2cc5becc5cf09473a9c8212fb8ac1ef))
* Run context guarded methods ([9309fb6](https://github.com/PurrNet/PurrNet/commit/9309fb64810a9595221e174d0ae21aa93ee93cce))

# [1.16.0-beta.56](https://github.com/PurrNet/PurrNet/compare/v1.16.0-beta.55...v1.16.0-beta.56) (2025-10-16)


### Bug Fixes

* Added git checking to addon library ([336e590](https://github.com/PurrNet/PurrNet/commit/336e59027c71b3ed96a6dc6d8f4f5a699a6af213))

# [1.16.0-beta.55](https://github.com/PurrNet/PurrNet/compare/v1.16.0-beta.54...v1.16.0-beta.55) (2025-10-10)


### Bug Fixes

* host disconnecting was despawning other player owned objects due to bad cache ([52e1717](https://github.com/PurrNet/PurrNet/commit/52e171765f8e2b3834ed9ea3178979bbdabfcce4))

# [1.16.0-beta.54](https://github.com/PurrNet/PurrNet/compare/v1.16.0-beta.53...v1.16.0-beta.54) (2025-10-10)


### Bug Fixes

* only adapt outside of editor ([cacc0c7](https://github.com/PurrNet/PurrNet/commit/cacc0c7bc9d501ee7bbbf65d1e1a028145182612))

# [1.16.0-beta.53](https://github.com/PurrNet/PurrNet/compare/v1.16.0-beta.52...v1.16.0-beta.53) (2025-10-10)


### Bug Fixes

* some more `try catch` and reset whitelist/blacklist state when pool reset called ([5d4958b](https://github.com/PurrNet/PurrNet/commit/5d4958b1bfe948c7c77431740ce15912c92a1a08))

# [1.16.0-beta.52](https://github.com/PurrNet/PurrNet/compare/v1.16.0-beta.51...v1.16.0-beta.52) (2025-10-10)


### Bug Fixes

* make sure any exceptions in the callbacks for synctypes dont break any flow AND that reset pool resets it's internal state ([de2a64c](https://github.com/PurrNet/PurrNet/commit/de2a64c216207ee4b495edab85fd253e3e3634f1))

# [1.16.0-beta.51](https://github.com/PurrNet/PurrNet/compare/v1.16.0-beta.50...v1.16.0-beta.51) (2025-10-08)


### Bug Fixes

* disposable Array/Hashset collections missing delta packers ([71b1059](https://github.com/PurrNet/PurrNet/commit/71b1059df8a2b1ddc37a31f55128c0fd27d86019))

# [1.16.0-beta.50](https://github.com/PurrNet/PurrNet/compare/v1.16.0-beta.49...v1.16.0-beta.50) (2025-10-08)


### Bug Fixes

* hashset Create and obsolete constructor ([5c35273](https://github.com/PurrNet/PurrNet/commit/5c35273b030333786f95358601fc68cda7609520))

# [1.16.0-beta.49](https://github.com/PurrNet/PurrNet/compare/v1.16.0-beta.48...v1.16.0-beta.49) (2025-10-07)


### Bug Fixes

* disposable list myers action application ([e5c3160](https://github.com/PurrNet/PurrNet/commit/e5c3160641a9bea983eb6ef5548359196066e86a))

# [1.16.0-beta.48](https://github.com/PurrNet/PurrNet/compare/v1.16.0-beta.47...v1.16.0-beta.48) (2025-10-07)


### Bug Fixes

* delta packing registration was faulty ([3db16b8](https://github.com/PurrNet/PurrNet/commit/3db16b844ee0e4379debed08f50f652506235586))

# [1.16.0-beta.47](https://github.com/PurrNet/PurrNet/compare/v1.16.0-beta.46...v1.16.0-beta.47) (2025-10-07)


### Bug Fixes

* ulong packing ([c48f735](https://github.com/PurrNet/PurrNet/commit/c48f7354620cf346eba2ffa95ed529b93fb99517))

# [1.16.0-beta.46](https://github.com/PurrNet/PurrNet/compare/v1.16.0-beta.45...v1.16.0-beta.46) (2025-10-07)


### Bug Fixes

* packer was rounding floats for CompressedFloat when it doesn't have to ([e968119](https://github.com/PurrNet/PurrNet/commit/e9681190fd2e6dcb1b91378757d8e6ce548d1942))

# [1.16.0-beta.45](https://github.com/PurrNet/PurrNet/compare/v1.16.0-beta.44...v1.16.0-beta.45) (2025-10-07)


### Bug Fixes

* compressed float equality failed, stick to storing raw value instead of float value ([2d04a76](https://github.com/PurrNet/PurrNet/commit/2d04a76d83fedd6a2c9f93e10d6c23338d92cb12))

# [1.16.0-beta.44](https://github.com/PurrNet/PurrNet/compare/v1.16.0-beta.43...v1.16.0-beta.44) (2025-10-07)


### Bug Fixes

* add debugging scripting symbol for delta compression that packs extra data to let you know of issues ([2c96204](https://github.com/PurrNet/PurrNet/commit/2c9620425c2ae43e545bbc04ed94205f02e75d6e))

# [1.16.0-beta.43](https://github.com/PurrNet/PurrNet/compare/v1.16.0-beta.42...v1.16.0-beta.43) (2025-10-05)


### Bug Fixes

* unit tests circucal reference issue ([c9c0624](https://github.com/PurrNet/PurrNet/commit/c9c0624dc3715c6ed3f95b957b05c79046344fb2))

# [1.16.0-beta.42](https://github.com/PurrNet/PurrNet/compare/v1.16.0-beta.41...v1.16.0-beta.42) (2025-10-04)


### Bug Fixes

* disposable dictionary serialization fixes ([0a93474](https://github.com/PurrNet/PurrNet/commit/0a934746bda14c997c2aaaf3948fee32c81f8fd0))

# [1.16.0-beta.41](https://github.com/PurrNet/PurrNet/compare/v1.16.0-beta.40...v1.16.0-beta.41) (2025-10-02)


### Bug Fixes

* make sure the packer has the proper data when communicating to others ([dc9f03c](https://github.com/PurrNet/PurrNet/commit/dc9f03cd39b184cd261016f187bd72a872a0e44e))

# [1.16.0-beta.40](https://github.com/PurrNet/PurrNet/compare/v1.16.0-beta.39...v1.16.0-beta.40) (2025-09-30)


### Bug Fixes

* packing for DisposableArray.cs ([1e5b4d2](https://github.com/PurrNet/PurrNet/commit/1e5b4d20907f09d45c03459c7396f8005dd12c7c))

# [1.16.0-beta.39](https://github.com/PurrNet/PurrNet/compare/v1.16.0-beta.38...v1.16.0-beta.39) (2025-09-29)


### Bug Fixes

* make sure disposable list is registered for dictionary ([34e461a](https://github.com/PurrNet/PurrNet/commit/34e461a6adea9617d29f41c740bda036a84b677e))

# [1.16.0-beta.38](https://github.com/PurrNet/PurrNet/compare/v1.16.0-beta.37...v1.16.0-beta.38) (2025-09-28)


### Bug Fixes

* attempting to make networktransform more performant ([6a065f2](https://github.com/PurrNet/PurrNet/commit/6a065f210f1d2a6ad238c6a057c512d53fa93b66))

# [1.16.0-beta.37](https://github.com/PurrNet/PurrNet/compare/v1.16.0-beta.36...v1.16.0-beta.37) (2025-09-28)


### Bug Fixes

* sorting was backwards ([880a670](https://github.com/PurrNet/PurrNet/commit/880a67069036afed16757245dc6fdf2a58b336d8))

# [1.16.0-beta.36](https://github.com/PurrNet/PurrNet/compare/v1.16.0-beta.35...v1.16.0-beta.36) (2025-09-28)


### Bug Fixes

* allow to prioritize non generated packers more explicitly ([0081fbc](https://github.com/PurrNet/PurrNet/commit/0081fbc4d9a25432ce31ac9807dea7ea0a151ab4))

# [1.16.0-beta.35](https://github.com/PurrNet/PurrNet/compare/v1.16.0-beta.34...v1.16.0-beta.35) (2025-09-26)


### Bug Fixes

* new myers deltalist packer ([f8c6d05](https://github.com/PurrNet/PurrNet/commit/f8c6d058e8c192e3355fe7d768a2ef32825e0ca9))

# [1.16.0-beta.34](https://github.com/PurrNet/PurrNet/compare/v1.16.0-beta.33...v1.16.0-beta.34) (2025-09-25)


### Bug Fixes

* myers impl ([61b1929](https://github.com/PurrNet/PurrNet/commit/61b19299f17a9d48377a7ad73791c039a63bda45))

# [1.16.0-beta.33](https://github.com/PurrNet/PurrNet/compare/v1.16.0-beta.32...v1.16.0-beta.33) (2025-09-25)


### Bug Fixes

* some packing bugs ([fc9899a](https://github.com/PurrNet/PurrNet/commit/fc9899a1fc8fe9aad5e24aca35b5c429346572b9))

# [1.16.0-beta.32](https://github.com/PurrNet/PurrNet/compare/v1.16.0-beta.31...v1.16.0-beta.32) (2025-09-25)


### Bug Fixes

* better error for unitask ([60e6ea2](https://github.com/PurrNet/PurrNet/commit/60e6ea222bc636c3f155d6c9329d0c62ba4cd069))

# [1.16.0-beta.31](https://github.com/PurrNet/PurrNet/compare/v1.16.0-beta.30...v1.16.0-beta.31) (2025-09-25)


### Bug Fixes

* include sceneid for spawn point provider ([2c64600](https://github.com/PurrNet/PurrNet/commit/2c646000874c61432005038c39ed9fe13543e7cd))

# [1.16.0-beta.30](https://github.com/PurrNet/PurrNet/compare/v1.16.0-beta.29...v1.16.0-beta.30) (2025-09-25)


### Bug Fixes

* if empty list ([d5c77f3](https://github.com/PurrNet/PurrNet/commit/d5c77f3a2d4dcabc145bdb6ef7ca8333ca99ec40))

# [1.16.0-beta.29](https://github.com/PurrNet/PurrNet/compare/v1.16.0-beta.28...v1.16.0-beta.29) (2025-09-25)


### Bug Fixes

* messaging issue ([2a8c0cb](https://github.com/PurrNet/PurrNet/commit/2a8c0cbf684636d617f85a4eae141e0f0e52b305))

# [1.16.0-beta.28](https://github.com/PurrNet/PurrNet/compare/v1.16.0-beta.27...v1.16.0-beta.28) (2025-09-25)


### Bug Fixes

* spawn point provider pattern ([7e20abe](https://github.com/PurrNet/PurrNet/commit/7e20abe1a733511572bb231044dcc4985f38027f))

# [1.16.0-beta.27](https://github.com/PurrNet/PurrNet/compare/v1.16.0-beta.26...v1.16.0-beta.27) (2025-09-25)


### Bug Fixes

* more optimizations ([7e777de](https://github.com/PurrNet/PurrNet/commit/7e777ded30420e69a00fb6f7a020d6a3893baa0a))

# [1.16.0-beta.26](https://github.com/PurrNet/PurrNet/compare/v1.16.0-beta.25...v1.16.0-beta.26) (2025-09-25)


### Bug Fixes

* improved some packing for the NT ([e7eab45](https://github.com/PurrNet/PurrNet/commit/e7eab45db557b28cbe57e0f245809173f8697f6d))

# [1.16.0-beta.25](https://github.com/PurrNet/PurrNet/compare/v1.16.0-beta.24...v1.16.0-beta.25) (2025-09-25)


### Bug Fixes

* +disposable array ([4c92504](https://github.com/PurrNet/PurrNet/commit/4c925042940abf14b839d1b5b6a7063a56be34b2))
* allow to set extra bones for the NetworkBones component ([7e7872d](https://github.com/PurrNet/PurrNet/commit/7e7872d60c733c7d135a43a95e7c3d6f0a4b70d3))
* some packing issues ([f43ee8a](https://github.com/PurrNet/PurrNet/commit/f43ee8a8220c908fda10f94bad6d78ed9c0967ca))

# [1.16.0-beta.24](https://github.com/PurrNet/PurrNet/compare/v1.16.0-beta.23...v1.16.0-beta.24) (2025-09-23)


### Bug Fixes

* Added statistics manager display target options ([75d095f](https://github.com/PurrNet/PurrNet/commit/75d095ff1881ed47d836c9af63baf21c90bc09e9))

# [1.16.0-beta.23](https://github.com/PurrNet/PurrNet/compare/v1.16.0-beta.22...v1.16.0-beta.23) (2025-09-23)


### Bug Fixes

* compiler error for network module due to rework ([2a5a3e5](https://github.com/PurrNet/PurrNet/commit/2a5a3e5701011880acb580f392b2fdbbba673a88))

# [1.16.0-beta.22](https://github.com/PurrNet/PurrNet/compare/v1.16.0-beta.21...v1.16.0-beta.22) (2025-09-23)


### Bug Fixes

* GC when validating rpcs ([adeca8a](https://github.com/PurrNet/PurrNet/commit/adeca8a34f869cb39e1cd7700ecc2bbee22cb438))

# [1.16.0-beta.21](https://github.com/PurrNet/PurrNet/compare/v1.16.0-beta.20...v1.16.0-beta.21) (2025-09-22)


### Bug Fixes

* Linked network prefabs logic added ([ba4a2e4](https://github.com/PurrNet/PurrNet/commit/ba4a2e4c4fa2e60e0d86ed7d8e6f3609fded1662))

# [1.16.0-beta.20](https://github.com/PurrNet/PurrNet/compare/v1.16.0-beta.19...v1.16.0-beta.20) (2025-09-22)


### Bug Fixes

* make sure scene is valid when unloading ([0d7e8a3](https://github.com/PurrNet/PurrNet/commit/0d7e8a324237d8eec7faa8c7f3c0c0a743ff0553))

# [1.16.0-beta.19](https://github.com/PurrNet/PurrNet/compare/v1.16.0-beta.18...v1.16.0-beta.19) (2025-09-21)


### Bug Fixes

* reflection getmethod failing ([568db6c](https://github.com/PurrNet/PurrNet/commit/568db6c39656b5aa315756127740db3c3b1a9f95))

# [1.16.0-beta.18](https://github.com/PurrNet/PurrNet/compare/v1.16.0-beta.17...v1.16.0-beta.18) (2025-09-21)


### Bug Fixes

* Added PurrChat link to toolbar ([72d3d58](https://github.com/PurrNet/PurrNet/commit/72d3d58cf6be44a925837f736c49b7a1af92e93b))

# [1.16.0-beta.17](https://github.com/PurrNet/PurrNet/compare/v1.16.0-beta.16...v1.16.0-beta.17) (2025-09-17)


### Bug Fixes

* some host issues with visibility rules ([29476a0](https://github.com/PurrNet/PurrNet/commit/29476a08b5626e1e5b84cb69c6498ab52af084db))

# [1.16.0-beta.16](https://github.com/PurrNet/PurrNet/compare/v1.16.0-beta.15...v1.16.0-beta.16) (2025-09-17)


### Features

* Run context guarded methods ([9309fb6](https://github.com/PurrNet/PurrNet/commit/9309fb64810a9595221e174d0ae21aa93ee93cce))

# [1.16.0-beta.15](https://github.com/PurrNet/PurrNet/compare/v1.16.0-beta.14...v1.16.0-beta.15) (2025-09-14)


### Bug Fixes

* set position after parenting ([f7d4dbf](https://github.com/PurrNet/PurrNet/commit/f7d4dbfdc7fe8a17f8dd9a15cf2a5393f0cac25a))

# [1.16.0-beta.14](https://github.com/PurrNet/PurrNet/compare/v1.16.0-beta.13...v1.16.0-beta.14) (2025-09-10)


### Bug Fixes

* steam errors when trying to use connection after closed ([ee4ed6a](https://github.com/PurrNet/PurrNet/commit/ee4ed6ab6c540d85aaf51ed0f5af29d43508e3ce))

# [1.16.0-beta.13](https://github.com/PurrNet/PurrNet/compare/v1.16.0-beta.12...v1.16.0-beta.13) (2025-09-10)


### Bug Fixes

* include local pos for child pieces ([4c67434](https://github.com/PurrNet/PurrNet/commit/4c67434b0fa854943a8ca84073931457b83f639c))

# [1.16.0-beta.12](https://github.com/PurrNet/PurrNet/compare/v1.16.0-beta.11...v1.16.0-beta.12) (2025-09-09)


### Bug Fixes

* naive delta packer for array and list ([4904eaa](https://github.com/PurrNet/PurrNet/commit/4904eaa69a2b5540b4d81b1a3fdf6000806a77f3))

# [1.16.0-beta.11](https://github.com/PurrNet/PurrNet/compare/v1.16.0-beta.10...v1.16.0-beta.11) (2025-09-07)


### Bug Fixes

* some delta packing for spawn packet batches ([20388f8](https://github.com/PurrNet/PurrNet/commit/20388f8ef8d4c38c66d3a8fd09e008a4647a1499))

# [1.16.0-beta.10](https://github.com/PurrNet/PurrNet/compare/v1.16.0-beta.9...v1.16.0-beta.10) (2025-09-05)


### Features

* Added PlayerIdentity ([93ffe55](https://github.com/PurrNet/PurrNet/commit/93ffe558807b310e344348986d8aab4755893633))

# [1.16.0-beta.9](https://github.com/PurrNet/PurrNet/compare/v1.16.0-beta.8...v1.16.0-beta.9) (2025-09-05)


### Bug Fixes

* network transform module bug ([b6a0a5d](https://github.com/PurrNet/PurrNet/commit/b6a0a5d747f0369d9316123b8619e376f97572e8))

# [1.16.0-beta.8](https://github.com/PurrNet/PurrNet/compare/v1.16.0-beta.7...v1.16.0-beta.8) (2025-09-04)


### Bug Fixes

* purrtransport compiler error ([62713c5](https://github.com/PurrNet/PurrNet/commit/62713c51f0b792fc762a91725cdc469d00924c75))

# [1.16.0-beta.7](https://github.com/PurrNet/PurrNet/compare/v1.16.0-beta.6...v1.16.0-beta.7) (2025-09-04)


### Bug Fixes

* don't send irrelevant data for the NT ([315c331](https://github.com/PurrNet/PurrNet/commit/315c33131f45e9faa94e5047814438290f561869))

# [1.16.0-beta.6](https://github.com/PurrNet/PurrNet/compare/v1.16.0-beta.5...v1.16.0-beta.6) (2025-09-03)


### Bug Fixes

* resolve hostname for the udp transport ([cc86356](https://github.com/PurrNet/PurrNet/commit/cc86356a681413e26bab57564c49d45dd6a8808d))

# [1.16.0-beta.5](https://github.com/PurrNet/PurrNet/compare/v1.16.0-beta.4...v1.16.0-beta.5) (2025-09-02)


### Bug Fixes

* handle isServer scenario differently ([d7a930e](https://github.com/PurrNet/PurrNet/commit/d7a930e60060ecf1dbbc56159731420c4edcc047))

# [1.16.0-beta.4](https://github.com/PurrNet/PurrNet/compare/v1.16.0-beta.3...v1.16.0-beta.4) (2025-09-02)


### Bug Fixes

* NetworkTransform `ForceSync` was weird ([c2593fd](https://github.com/PurrNet/PurrNet/commit/c2593fdb3b3b8a019f851302df1016a711386a9f))

# [1.16.0-beta.3](https://github.com/PurrNet/PurrNet/compare/v1.16.0-beta.2...v1.16.0-beta.3) (2025-09-02)


### Features

* purrtransport udp support ([1a0ad4a](https://github.com/PurrNet/PurrNet/commit/1a0ad4ada2cc5becc5cf09473a9c8212fb8ac1ef))

# [1.16.0-beta.2](https://github.com/PurrNet/PurrNet/compare/v1.16.0-beta.1...v1.16.0-beta.2) (2025-09-02)


### Bug Fixes

* allow network animator to reconcile time ([caa62bc](https://github.com/PurrNet/PurrNet/commit/caa62bc81078a38cf5381dad5f888e6186ee3089))

# [1.16.0-beta.1](https://github.com/PurrNet/PurrNet/compare/v1.15.1-beta.5...v1.16.0-beta.1) (2025-09-01)


### Features

* IStandaloneSerializable ([27e1733](https://github.com/PurrNet/PurrNet/commit/27e17337811855f0b0e5d486416bb1713cffe333))

## [1.15.1-beta.5](https://github.com/PurrNet/PurrNet/compare/v1.15.1-beta.4...v1.15.1-beta.5) (2025-08-28)


### Bug Fixes

* add `HierarchyV2.onPreSpawn` static event ([0c02749](https://github.com/PurrNet/PurrNet/commit/0c0274922d2cfb1db576c4bb4fbfa4d1e73f50f6))

## [1.15.1-beta.4](https://github.com/PurrNet/PurrNet/compare/v1.15.1-beta.3...v1.15.1-beta.4) (2025-08-28)


### Bug Fixes

* SyncTimer issues ([4d8e7f4](https://github.com/PurrNet/PurrNet/commit/4d8e7f4fc00fb9429eecb74727f73206d0d1350b))

## [1.15.1-beta.3](https://github.com/PurrNet/PurrNet/compare/v1.15.1-beta.2...v1.15.1-beta.3) (2025-08-28)


### Bug Fixes

* unity version issues ([f8c90e2](https://github.com/PurrNet/PurrNet/commit/f8c90e2ddcd22c1a2a0dc94c427b9041619d1205))

## [1.15.1-beta.2](https://github.com/PurrNet/PurrNet/compare/v1.15.1-beta.1...v1.15.1-beta.2) (2025-08-27)


### Bug Fixes

* PurrTransport cache made changing master server a pain ([f642aff](https://github.com/PurrNet/PurrNet/commit/f642aff74b635176dcb1036b2a54f5909f42874b))

## [1.15.1-beta.1](https://github.com/PurrNet/PurrNet/compare/v1.15.0...v1.15.1-beta.1) (2025-08-25)


### Bug Fixes

* cleanup issues ([36abd59](https://github.com/PurrNet/PurrNet/commit/36abd590f60caa6e79d7788e4871805b1014ab0e))

# [1.15.0](https://github.com/PurrNet/PurrNet/compare/v1.14.1...v1.15.0) (2025-08-25)


### Bug Fixes

* allow to filter purrnet's scene object discovery ([522ef9d](https://github.com/PurrNet/PurrNet/commit/522ef9d042983d83d886c63d05342a7a704d0f50))
* allow to skip scene auto spawning ([204987c](https://github.com/PurrNet/PurrNet/commit/204987c50ded5d0b76dc5a83a6c7a8e264a95c80))
* avoid reading data if bones aren't ready ([fca1a62](https://github.com/PurrNet/PurrNet/commit/fca1a62a2bba25bf840853afdf3bc7915e5d569d))
* better internal packer resizing calc ([ea6f39d](https://github.com/PurrNet/PurrNet/commit/ea6f39df6c0f66e242e183c083de1c6788f586db))
* cleanup modules ([729fc3a](https://github.com/PurrNet/PurrNet/commit/729fc3a8330aef563fe1c4d2d15f583999506403))
* dispose bones when destroying object ([f92b774](https://github.com/PurrNet/PurrNet/commit/f92b774e412c93c6c4c051bc23744ad66d84fd8e))
* don't put `skipSceneAutoSpawning` in the pool ([e84f639](https://github.com/PurrNet/PurrNet/commit/e84f639b77fcea0922f252768de8812bf8f77857))
* filter shouldn't be as broad as a GO ([9c1597f](https://github.com/PurrNet/PurrNet/commit/9c1597faadb2e7c208d1a3e3c2e835ce24e9114b))
* networkbones courtesy of Resolute Games ([896a018](https://github.com/PurrNet/PurrNet/commit/896a01876af0d372c6b3723004e1e78bf99fa9e3))
* pack unity LayerMask ([a23e8dd](https://github.com/PurrNet/PurrNet/commit/a23e8ddc64b88ed17142b63eb07449f89f88ef1a))
* scene load events ([63dbc5c](https://github.com/PurrNet/PurrNet/commit/63dbc5cbbc306c9175230b37209a98c3397cc07c))
* UDP transport reconnection ([c15c6a5](https://github.com/PurrNet/PurrNet/commit/c15c6a5704c4fc83f990de0b42533fae77b7fb3c))
* unity 6 color thingy ([4344e4e](https://github.com/PurrNet/PurrNet/commit/4344e4e3026351967944c00c19a98c5fac29d3aa))


### Features

* add CompressedVector2 for 2D vector compression ([57a0213](https://github.com/PurrNet/PurrNet/commit/57a021325b3734f60ba37ed4ab1eee4703594501))
* Add implicit conversion operators for CompressedVector3 <-> Vector2 ([b44f7b5](https://github.com/PurrNet/PurrNet/commit/b44f7b5a2463aa7ea50affd6339244d1196f6885))
* allow to enable/disable purr buttons ([7d37c56](https://github.com/PurrNet/PurrNet/commit/7d37c5693171ce82217cdd978a4084c53323effa))
* endian checks ([5ebbe9f](https://github.com/PurrNet/PurrNet/commit/5ebbe9f220b9790413f42e72151629ec4788ce40))

# [1.15.0-beta.8](https://github.com/PurrNet/PurrNet/compare/v1.15.0-beta.7...v1.15.0-beta.8) (2025-08-24)


### Bug Fixes

* cleanup modules ([729fc3a](https://github.com/PurrNet/PurrNet/commit/729fc3a8330aef563fe1c4d2d15f583999506403))

# [1.15.0-beta.7](https://github.com/PurrNet/PurrNet/compare/v1.15.0-beta.6...v1.15.0-beta.7) (2025-08-23)


### Bug Fixes

* don't put `skipSceneAutoSpawning` in the pool ([e84f639](https://github.com/PurrNet/PurrNet/commit/e84f639b77fcea0922f252768de8812bf8f77857))

# [1.15.0-beta.6](https://github.com/PurrNet/PurrNet/compare/v1.15.0-beta.5...v1.15.0-beta.6) (2025-08-23)


### Bug Fixes

* better internal packer resizing calc ([ea6f39d](https://github.com/PurrNet/PurrNet/commit/ea6f39df6c0f66e242e183c083de1c6788f586db))

# [1.15.0-beta.5](https://github.com/PurrNet/PurrNet/compare/v1.15.0-beta.4...v1.15.0-beta.5) (2025-08-23)


### Features

* endian checks ([5ebbe9f](https://github.com/PurrNet/PurrNet/commit/5ebbe9f220b9790413f42e72151629ec4788ce40))

# [1.15.0-beta.4](https://github.com/PurrNet/PurrNet/compare/v1.15.0-beta.3...v1.15.0-beta.4) (2025-08-23)


### Features

* allow to enable/disable purr buttons ([7d37c56](https://github.com/PurrNet/PurrNet/commit/7d37c5693171ce82217cdd978a4084c53323effa))

# [1.15.0-beta.3](https://github.com/PurrNet/PurrNet/compare/v1.15.0-beta.2...v1.15.0-beta.3) (2025-08-23)


### Bug Fixes

* scene load events ([63dbc5c](https://github.com/PurrNet/PurrNet/commit/63dbc5cbbc306c9175230b37209a98c3397cc07c))

# [1.15.0-beta.2](https://github.com/PurrNet/PurrNet/compare/v1.15.0-beta.1...v1.15.0-beta.2) (2025-08-23)


### Bug Fixes

* allow to skip scene auto spawning ([204987c](https://github.com/PurrNet/PurrNet/commit/204987c50ded5d0b76dc5a83a6c7a8e264a95c80))

# [1.15.0-beta.1](https://github.com/PurrNet/PurrNet/compare/v1.14.2-beta.8...v1.15.0-beta.1) (2025-08-22)


### Features

* add CompressedVector2 for 2D vector compression ([57a0213](https://github.com/PurrNet/PurrNet/commit/57a021325b3734f60ba37ed4ab1eee4703594501))
* Add implicit conversion operators for CompressedVector3 <-> Vector2 ([b44f7b5](https://github.com/PurrNet/PurrNet/commit/b44f7b5a2463aa7ea50affd6339244d1196f6885))

## [1.14.2-beta.8](https://github.com/PurrNet/PurrNet/compare/v1.14.2-beta.7...v1.14.2-beta.8) (2025-08-22)


### Bug Fixes

* avoid reading data if bones aren't ready ([fca1a62](https://github.com/PurrNet/PurrNet/commit/fca1a62a2bba25bf840853afdf3bc7915e5d569d))

## [1.14.2-beta.7](https://github.com/PurrNet/PurrNet/compare/v1.14.2-beta.6...v1.14.2-beta.7) (2025-08-21)


### Bug Fixes

* filter shouldn't be as broad as a GO ([9c1597f](https://github.com/PurrNet/PurrNet/commit/9c1597faadb2e7c208d1a3e3c2e835ce24e9114b))

## [1.14.2-beta.6](https://github.com/PurrNet/PurrNet/compare/v1.14.2-beta.5...v1.14.2-beta.6) (2025-08-21)


### Bug Fixes

* allow to filter purrnet's scene object discovery ([522ef9d](https://github.com/PurrNet/PurrNet/commit/522ef9d042983d83d886c63d05342a7a704d0f50))

## [1.14.2-beta.5](https://github.com/PurrNet/PurrNet/compare/v1.14.2-beta.4...v1.14.2-beta.5) (2025-08-20)


### Bug Fixes

* UDP transport reconnection ([c15c6a5](https://github.com/PurrNet/PurrNet/commit/c15c6a5704c4fc83f990de0b42533fae77b7fb3c))

## [1.14.2-beta.4](https://github.com/PurrNet/PurrNet/compare/v1.14.2-beta.3...v1.14.2-beta.4) (2025-08-20)


### Bug Fixes

* unity 6 color thingy ([4344e4e](https://github.com/PurrNet/PurrNet/commit/4344e4e3026351967944c00c19a98c5fac29d3aa))

## [1.14.2-beta.3](https://github.com/PurrNet/PurrNet/compare/v1.14.2-beta.2...v1.14.2-beta.3) (2025-08-19)


### Bug Fixes

* pack unity LayerMask ([a23e8dd](https://github.com/PurrNet/PurrNet/commit/a23e8ddc64b88ed17142b63eb07449f89f88ef1a))

## [1.14.2-beta.2](https://github.com/PurrNet/PurrNet/compare/v1.14.2-beta.1...v1.14.2-beta.2) (2025-08-18)


### Bug Fixes

* networkbones courtesy of Resolute Games ([896a018](https://github.com/PurrNet/PurrNet/commit/896a01876af0d372c6b3723004e1e78bf99fa9e3))

## [1.14.2-beta.1](https://github.com/PurrNet/PurrNet/compare/v1.14.1...v1.14.2-beta.1) (2025-08-17)


### Bug Fixes

* dispose bones when destroying object ([f92b774](https://github.com/PurrNet/PurrNet/commit/f92b774e412c93c6c4c051bc23744ad66d84fd8e))

## [1.14.1](https://github.com/PurrNet/PurrNet/compare/v1.14.0...v1.14.1) (2025-08-16)


### Bug Fixes

* Addon library fixed for manifest handling ([7b13f01](https://github.com/PurrNet/PurrNet/commit/7b13f013218777dc32b4536539915a21411e8e2c))
* Addon library image request handling improved ([49eccf7](https://github.com/PurrNet/PurrNet/commit/49eccf794307569694ad47d794a70ccca02cd322))
* buffer settings for bones ([f4af0eb](https://github.com/PurrNet/PurrNet/commit/f4af0ebbace94885acd8c51e5b9c20ad32d1ce6b))
* NetworkBones adjustments ([5a61a86](https://github.com/PurrNet/PurrNet/commit/5a61a869a55c057609b05773acc9594908ea433c))

## [1.14.1-beta.4](https://github.com/PurrNet/PurrNet/compare/v1.14.1-beta.3...v1.14.1-beta.4) (2025-08-16)


### Bug Fixes

* Addon library image request handling improved ([49eccf7](https://github.com/PurrNet/PurrNet/commit/49eccf794307569694ad47d794a70ccca02cd322))

## [1.14.1-beta.3](https://github.com/PurrNet/PurrNet/compare/v1.14.1-beta.2...v1.14.1-beta.3) (2025-08-16)


### Bug Fixes

* Addon library fixed for manifest handling ([7b13f01](https://github.com/PurrNet/PurrNet/commit/7b13f013218777dc32b4536539915a21411e8e2c))

## [1.14.1-beta.2](https://github.com/PurrNet/PurrNet/compare/v1.14.1-beta.1...v1.14.1-beta.2) (2025-08-16)


### Bug Fixes

* buffer settings for bones ([f4af0eb](https://github.com/PurrNet/PurrNet/commit/f4af0ebbace94885acd8c51e5b9c20ad32d1ce6b))

## [1.14.1-beta.1](https://github.com/PurrNet/PurrNet/compare/v1.14.0...v1.14.1-beta.1) (2025-08-16)


### Bug Fixes

* NetworkBones adjustments ([5a61a86](https://github.com/PurrNet/PurrNet/commit/5a61a869a55c057609b05773acc9594908ea433c))

# [1.14.0](https://github.com/PurrNet/PurrNet/compare/v1.13.3...v1.14.0) (2025-08-15)


### Bug Fixes

* actual il fixes ([6a8176e](https://github.com/PurrNet/PurrNet/commit/6a8176e99ebb8ae73c7a6cd99d98001cadfe2248))
* allow DontPack to be at the type level ([354f271](https://github.com/PurrNet/PurrNet/commit/354f27178bc420b279c50c79a01e7c02fb09e2b5))
* allow for null values when reading classes with inheritance ([86db192](https://github.com/PurrNet/PurrNet/commit/86db1929d3d81367e362effb6537fa28abc511ac))
* allow for value modifiers for the delta module ([c0ddf66](https://github.com/PurrNet/PurrNet/commit/c0ddf665067973d5d28c2c63ad01a4a8c941ce1f))
* also cleanup on destroy ([2d47451](https://github.com/PurrNet/PurrNet/commit/2d47451772812bf5caf09c6c7af4e69d602c3485))
* delta list packing ([7392b4b](https://github.com/PurrNet/PurrNet/commit/7392b4b6e276ae91cde72dd2b3c509194cc1e1ab))
* disposable list packer issue ([23598b9](https://github.com/PurrNet/PurrNet/commit/23598b9192b0f0402d1487d7e84644d14b4f97f4))
* dont create a server object until we need it since it causes issues ([801db42](https://github.com/PurrNet/PurrNet/commit/801db425600b41bf6eb0b9fc295edb9d082324b2))
* if we hit cleanup from `OnDisable` force close the connection ([318c5ed](https://github.com/PurrNet/PurrNet/commit/318c5edfd59fe0af734821cdd23c7dadde524b69))
* il error ([5e27ed6](https://github.com/PurrNet/PurrNet/commit/5e27ed62c9bfdfe0d3644e2e6dd5ae14d09018b7))
* IL generic resolving ([9f04291](https://github.com/PurrNet/PurrNet/commit/9f042912b8628a53384195d30c051f8974fa1af9))
* make `DontPack` attribute skip creating generators entirely if at the class level ([e18bf9c](https://github.com/PurrNet/PurrNet/commit/e18bf9c2e77658f33e9d8b1756dffd050306e33e))
* mark manual spawns such that we handle them differently (like not populating observers automatically) ([2dc77b8](https://github.com/PurrNet/PurrNet/commit/2dc77b8d8bc358902a5d407ddd8dcc5475de91f6))
* more modifier delta packing fixes ([c604c50](https://github.com/PurrNet/PurrNet/commit/c604c50fd946ff5460fe63e7921e455885ea42eb))
* more robust register calling and skipping of assemblies that don't refrence the purrnet assembly ([5daec62](https://github.com/PurrNet/PurrNet/commit/5daec625ecb0c3cb405162afc2bcdb772f170d81))
* return value of ValueModifier wasnt necessary ([ed0d668](https://github.com/PurrNet/PurrNet/commit/ed0d668b7ade468ca9024527d7bae92a2c5980d0))
* rework how RPC are called ([0f3c4f1](https://github.com/PurrNet/PurrNet/commit/0f3c4f1cfe992a89ca719afe53fd6e167c840d72))
* Statistics manager versioning position fix ([1539c54](https://github.com/PurrNet/PurrNet/commit/1539c54a46659045893b82665387e52c6bfaca51))
* Sync List null handling issue ([5704d83](https://github.com/PurrNet/PurrNet/commit/5704d83add83ced6dbab7138cfb3ea0d8f09fe8e))
* syncvar equality check regression ([d280ed5](https://github.com/PurrNet/PurrNet/commit/d280ed58bb1109a4f4622ff393271edc4da4e9ed))
* testing NetworkBones component ([7bcd4d5](https://github.com/PurrNet/PurrNet/commit/7bcd4d598cfca35fbfd19d569fd3ebe2cfdfe40b))
* type error deeper error message ([06904ef](https://github.com/PurrNet/PurrNet/commit/06904ef17432e96fb2ea86705a0bfcbf3f173e46))
* UnityProxy fails if manager doesn't have prefab provider ([fd2e674](https://github.com/PurrNet/PurrNet/commit/fd2e6746c5c4798430c55c4e41d1ee1fe3806a04))
* write/read with modifier bad history ([b8a7e3c](https://github.com/PurrNet/PurrNet/commit/b8a7e3c2ee6fc04f49279fbbed66db7783793749))


### Features

* add Packer.HasPacker and DeltaPacker.HasPacker ([a643b7b](https://github.com/PurrNet/PurrNet/commit/a643b7b30895e2f3be34c925d77b2282a456be8d))
* allow to force ipv4 for web transport ([83756ea](https://github.com/PurrNet/PurrNet/commit/83756eae66255ae2e1abac7e0009690876f1e59b))
* allow to not delta compress certain fields ([f320274](https://github.com/PurrNet/PurrNet/commit/f32027485614946ff34d65e8cfc5f730304fe402))

# [1.14.0-beta.22](https://github.com/PurrNet/PurrNet/compare/v1.14.0-beta.21...v1.14.0-beta.22) (2025-08-14)


### Bug Fixes

* testing NetworkBones component ([7bcd4d5](https://github.com/PurrNet/PurrNet/commit/7bcd4d598cfca35fbfd19d569fd3ebe2cfdfe40b))

# [1.14.0-beta.21](https://github.com/PurrNet/PurrNet/compare/v1.14.0-beta.20...v1.14.0-beta.21) (2025-08-14)


### Bug Fixes

* allow for null values when reading classes with inheritance ([86db192](https://github.com/PurrNet/PurrNet/commit/86db1929d3d81367e362effb6537fa28abc511ac))

# [1.14.0-beta.20](https://github.com/PurrNet/PurrNet/compare/v1.14.0-beta.19...v1.14.0-beta.20) (2025-08-14)


### Bug Fixes

* dont create a server object until we need it since it causes issues ([801db42](https://github.com/PurrNet/PurrNet/commit/801db425600b41bf6eb0b9fc295edb9d082324b2))

# [1.14.0-beta.19](https://github.com/PurrNet/PurrNet/compare/v1.14.0-beta.18...v1.14.0-beta.19) (2025-08-14)


### Bug Fixes

* if we hit cleanup from `OnDisable` force close the connection ([318c5ed](https://github.com/PurrNet/PurrNet/commit/318c5edfd59fe0af734821cdd23c7dadde524b69))

# [1.14.0-beta.18](https://github.com/PurrNet/PurrNet/compare/v1.14.0-beta.17...v1.14.0-beta.18) (2025-08-14)


### Bug Fixes

* type error deeper error message ([06904ef](https://github.com/PurrNet/PurrNet/commit/06904ef17432e96fb2ea86705a0bfcbf3f173e46))

# [1.14.0-beta.17](https://github.com/PurrNet/PurrNet/compare/v1.14.0-beta.16...v1.14.0-beta.17) (2025-08-13)


### Bug Fixes

* delta list packing ([7392b4b](https://github.com/PurrNet/PurrNet/commit/7392b4b6e276ae91cde72dd2b3c509194cc1e1ab))

# [1.14.0-beta.16](https://github.com/PurrNet/PurrNet/compare/v1.14.0-beta.15...v1.14.0-beta.16) (2025-08-13)


### Bug Fixes

* more modifier delta packing fixes ([c604c50](https://github.com/PurrNet/PurrNet/commit/c604c50fd946ff5460fe63e7921e455885ea42eb))

# [1.14.0-beta.15](https://github.com/PurrNet/PurrNet/compare/v1.14.0-beta.14...v1.14.0-beta.15) (2025-08-13)


### Bug Fixes

* write/read with modifier bad history ([b8a7e3c](https://github.com/PurrNet/PurrNet/commit/b8a7e3c2ee6fc04f49279fbbed66db7783793749))

# [1.14.0-beta.14](https://github.com/PurrNet/PurrNet/compare/v1.14.0-beta.13...v1.14.0-beta.14) (2025-08-11)


### Bug Fixes

* mark manual spawns such that we handle them differently (like not populating observers automatically) ([2dc77b8](https://github.com/PurrNet/PurrNet/commit/2dc77b8d8bc358902a5d407ddd8dcc5475de91f6))

# [1.14.0-beta.13](https://github.com/PurrNet/PurrNet/compare/v1.14.0-beta.12...v1.14.0-beta.13) (2025-08-11)


### Bug Fixes

* IL generic resolving ([9f04291](https://github.com/PurrNet/PurrNet/commit/9f042912b8628a53384195d30c051f8974fa1af9))

# [1.14.0-beta.12](https://github.com/PurrNet/PurrNet/compare/v1.14.0-beta.11...v1.14.0-beta.12) (2025-08-10)


### Bug Fixes

* make `DontPack` attribute skip creating generators entirely if at the class level ([e18bf9c](https://github.com/PurrNet/PurrNet/commit/e18bf9c2e77658f33e9d8b1756dffd050306e33e))

# [1.14.0-beta.11](https://github.com/PurrNet/PurrNet/compare/v1.14.0-beta.10...v1.14.0-beta.11) (2025-08-10)


### Bug Fixes

* return value of ValueModifier wasnt necessary ([ed0d668](https://github.com/PurrNet/PurrNet/commit/ed0d668b7ade468ca9024527d7bae92a2c5980d0))

# [1.14.0-beta.10](https://github.com/PurrNet/PurrNet/compare/v1.14.0-beta.9...v1.14.0-beta.10) (2025-08-10)


### Bug Fixes

* allow for value modifiers for the delta module ([c0ddf66](https://github.com/PurrNet/PurrNet/commit/c0ddf665067973d5d28c2c63ad01a4a8c941ce1f))

# [1.14.0-beta.9](https://github.com/PurrNet/PurrNet/compare/v1.14.0-beta.8...v1.14.0-beta.9) (2025-08-10)


### Bug Fixes

* Sync List null handling issue ([5704d83](https://github.com/PurrNet/PurrNet/commit/5704d83add83ced6dbab7138cfb3ea0d8f09fe8e))

# [1.14.0-beta.8](https://github.com/PurrNet/PurrNet/compare/v1.14.0-beta.7...v1.14.0-beta.8) (2025-08-06)


### Bug Fixes

* actual il fixes ([6a8176e](https://github.com/PurrNet/PurrNet/commit/6a8176e99ebb8ae73c7a6cd99d98001cadfe2248))
* il error ([5e27ed6](https://github.com/PurrNet/PurrNet/commit/5e27ed62c9bfdfe0d3644e2e6dd5ae14d09018b7))

# [1.14.0-beta.7](https://github.com/PurrNet/PurrNet/compare/v1.14.0-beta.6...v1.14.0-beta.7) (2025-08-05)


### Bug Fixes

* syncvar equality check regression ([d280ed5](https://github.com/PurrNet/PurrNet/commit/d280ed58bb1109a4f4622ff393271edc4da4e9ed))

# [1.14.0-beta.6](https://github.com/PurrNet/PurrNet/compare/v1.14.0-beta.5...v1.14.0-beta.6) (2025-08-05)


### Features

* allow to force ipv4 for web transport ([83756ea](https://github.com/PurrNet/PurrNet/commit/83756eae66255ae2e1abac7e0009690876f1e59b))

# [1.14.0-beta.5](https://github.com/PurrNet/PurrNet/compare/v1.14.0-beta.4...v1.14.0-beta.5) (2025-08-05)


### Bug Fixes

* also cleanup on destroy ([2d47451](https://github.com/PurrNet/PurrNet/commit/2d47451772812bf5caf09c6c7af4e69d602c3485))

# [1.14.0-beta.4](https://github.com/PurrNet/PurrNet/compare/v1.14.0-beta.3...v1.14.0-beta.4) (2025-08-05)


### Bug Fixes

* UnityProxy fails if manager doesn't have prefab provider ([fd2e674](https://github.com/PurrNet/PurrNet/commit/fd2e6746c5c4798430c55c4e41d1ee1fe3806a04))

# [1.14.0-beta.3](https://github.com/PurrNet/PurrNet/compare/v1.14.0-beta.2...v1.14.0-beta.3) (2025-08-05)


### Features

* add Packer.HasPacker and DeltaPacker.HasPacker ([a643b7b](https://github.com/PurrNet/PurrNet/commit/a643b7b30895e2f3be34c925d77b2282a456be8d))

# [1.14.0-beta.2](https://github.com/PurrNet/PurrNet/compare/v1.14.0-beta.1...v1.14.0-beta.2) (2025-08-05)


### Bug Fixes

* disposable list packer issue ([23598b9](https://github.com/PurrNet/PurrNet/commit/23598b9192b0f0402d1487d7e84644d14b4f97f4))

# [1.14.0-beta.1](https://github.com/PurrNet/PurrNet/compare/v1.13.4-beta.4...v1.14.0-beta.1) (2025-08-05)


### Features

* allow to not delta compress certain fields ([f320274](https://github.com/PurrNet/PurrNet/commit/f32027485614946ff34d65e8cfc5f730304fe402))

## [1.13.4-beta.4](https://github.com/PurrNet/PurrNet/compare/v1.13.4-beta.3...v1.13.4-beta.4) (2025-08-05)


### Bug Fixes

* allow DontPack to be at the type level ([354f271](https://github.com/PurrNet/PurrNet/commit/354f27178bc420b279c50c79a01e7c02fb09e2b5))

## [1.13.4-beta.3](https://github.com/PurrNet/PurrNet/compare/v1.13.4-beta.2...v1.13.4-beta.3) (2025-08-04)


### Bug Fixes

* rework how RPC are called ([0f3c4f1](https://github.com/PurrNet/PurrNet/commit/0f3c4f1cfe992a89ca719afe53fd6e167c840d72))

## [1.13.4-beta.2](https://github.com/PurrNet/PurrNet/compare/v1.13.4-beta.1...v1.13.4-beta.2) (2025-08-04)


### Bug Fixes

* Statistics manager versioning position fix ([1539c54](https://github.com/PurrNet/PurrNet/commit/1539c54a46659045893b82665387e52c6bfaca51))

## [1.13.4-beta.1](https://github.com/PurrNet/PurrNet/compare/v1.13.3...v1.13.4-beta.1) (2025-08-03)


### Bug Fixes

* more robust register calling and skipping of assemblies that don't refrence the purrnet assembly ([5daec62](https://github.com/PurrNet/PurrNet/commit/5daec625ecb0c3cb405162afc2bcdb772f170d81))

## [1.13.3](https://github.com/PurrNet/PurrNet/compare/v1.13.2...v1.13.3) (2025-08-03)


### Bug Fixes

* build version info missing ([7befbe1](https://github.com/PurrNet/PurrNet/commit/7befbe1c4579a7ca3799d3d931a09860944af004))
* Dictionary pool domain reload safety added ([11c1c68](https://github.com/PurrNet/PurrNet/commit/11c1c68f366e955234e51730b1c35f5dc9d216dd))
* Merge pull request [#153](https://github.com/PurrNet/PurrNet/issues/153) from bookdude13/HasModule-Client-Fix ([b531534](https://github.com/PurrNet/PurrNet/commit/b5315344a4778626b039ea22fe7823bd9e74b834))
* packer caching problems ([878e7b9](https://github.com/PurrNet/PurrNet/commit/878e7b94b0389ec37b115b6c60f96ccc31a4f266))
* properly set scene as dirty ([15476e8](https://github.com/PurrNet/PurrNet/commit/15476e826b6a986dc51a1a9448b80e6a770b9943))
* version mismatch issue editor/build ([2ebe5a8](https://github.com/PurrNet/PurrNet/commit/2ebe5a8d841a3499fa9cb540ca1079f0fda48b4b))

## [1.13.3-beta.6](https://github.com/PurrNet/PurrNet/compare/v1.13.3-beta.5...v1.13.3-beta.6) (2025-08-03)


### Bug Fixes

* version mismatch issue editor/build ([2ebe5a8](https://github.com/PurrNet/PurrNet/commit/2ebe5a8d841a3499fa9cb540ca1079f0fda48b4b))

## [1.13.3-beta.5](https://github.com/PurrNet/PurrNet/compare/v1.13.3-beta.4...v1.13.3-beta.5) (2025-08-03)


### Bug Fixes

* properly set scene as dirty ([15476e8](https://github.com/PurrNet/PurrNet/commit/15476e826b6a986dc51a1a9448b80e6a770b9943))

## [1.13.3-beta.4](https://github.com/PurrNet/PurrNet/compare/v1.13.3-beta.3...v1.13.3-beta.4) (2025-08-03)


### Bug Fixes

* build version info missing ([7befbe1](https://github.com/PurrNet/PurrNet/commit/7befbe1c4579a7ca3799d3d931a09860944af004))

## [1.13.3-beta.3](https://github.com/PurrNet/PurrNet/compare/v1.13.3-beta.2...v1.13.3-beta.3) (2025-08-03)


### Bug Fixes

* packer caching problems ([878e7b9](https://github.com/PurrNet/PurrNet/commit/878e7b94b0389ec37b115b6c60f96ccc31a4f266))

## [1.13.3-beta.2](https://github.com/PurrNet/PurrNet/compare/v1.13.3-beta.1...v1.13.3-beta.2) (2025-08-03)


### Bug Fixes

* Merge pull request [#153](https://github.com/PurrNet/PurrNet/issues/153) from bookdude13/HasModule-Client-Fix ([b531534](https://github.com/PurrNet/PurrNet/commit/b5315344a4778626b039ea22fe7823bd9e74b834))

## [1.13.3-beta.1](https://github.com/PurrNet/PurrNet/compare/v1.13.2...v1.13.3-beta.1) (2025-08-02)


### Bug Fixes

* Dictionary pool domain reload safety added ([11c1c68](https://github.com/PurrNet/PurrNet/commit/11c1c68f366e955234e51730b1c35f5dc9d216dd))

## [1.13.2](https://github.com/PurrNet/PurrNet/compare/v1.13.1...v1.13.2) (2025-07-31)


### Bug Fixes

* handle the case where Transform is null when packing it ([51cc083](https://github.com/PurrNet/PurrNet/commit/51cc08347ac8da4c3fd361b455a5862f83d2c253))

## [1.13.2-beta.1](https://github.com/PurrNet/PurrNet/compare/v1.13.1...v1.13.2-beta.1) (2025-07-31)


### Bug Fixes

* handle the case where Transform is null when packing it ([51cc083](https://github.com/PurrNet/PurrNet/commit/51cc08347ac8da4c3fd361b455a5862f83d2c253))

## [1.13.1](https://github.com/PurrNet/PurrNet/compare/v1.13.0...v1.13.1) (2025-07-31)


### Bug Fixes

* forceing release ([43af913](https://github.com/PurrNet/PurrNet/commit/43af913f3051249721557030abafbb926eec2ede))

## [1.13.1-beta.1](https://github.com/PurrNet/PurrNet/compare/v1.13.0...v1.13.1-beta.1) (2025-07-31)


### Bug Fixes

* forceing release ([43af913](https://github.com/PurrNet/PurrNet/commit/43af913f3051249721557030abafbb926eec2ede))

# [2.0.0](https://github.com/PurrNet/PurrNet/compare/v1.12.4...v2.0.0) (2025-07-31)


### Bug Fixes

* `isIk` wasn't checking enough cases thx @OverGast ([4dd1c01](https://github.com/PurrNet/PurrNet/commit/4dd1c0133428365101c3bb28f087c9345ae0cc1e))
* add asServer for collider registration (rollback) ([92390cf](https://github.com/PurrNet/PurrNet/commit/92390cfda5053c55d005d28bd6c901ad6ee9af7b))
* Added connection UI example ([36be104](https://github.com/PurrNet/PurrNet/commit/36be104386c6237c5c6222a33b362906f30b4f32))
* Added create overloads for disposable types ([6650be1](https://github.com/PurrNet/PurrNet/commit/6650be1301666f0c87fa68d4a0bc6c7f0d9fcb4e))
* Added Disposable HashSet creation ([9f1b2e3](https://github.com/PurrNet/PurrNet/commit/9f1b2e3dfdf30cc56960b631dd56147ae7563671))
* Added proper asset post processing to network assets ([c10f7fe](https://github.com/PurrNet/PurrNet/commit/c10f7feade5ad191f5fea8b1d1a3f2d32a3f64ef))
* Additional safety added to packer of gameobject and transform ([20f3623](https://github.com/PurrNet/PurrNet/commit/20f36236b4d6929a8bf1956ae52175ce09ad7824))
* allow for manual despawning too ([0a01be8](https://github.com/PurrNet/PurrNet/commit/0a01be8322a43085da4a0e8b9a9f22de16033ee5))
* allow to dynamically register colliders for rollback history ([2d2762b](https://github.com/PurrNet/PurrNet/commit/2d2762b493afcc604c9e7e3e7fa3a24c50c42125))
* also render purrnet toolbar on clones ([c3dbb1c](https://github.com/PurrNet/PurrNet/commit/c3dbb1cd127403cba11f5a9c415a82e6679b38e4))
* attempt at fixing steam issue ([6c96b84](https://github.com/PurrNet/PurrNet/commit/6c96b8432ce3b6a94f29da7d01f06110bea646b4))
* attempt to circumvent caching ([de0c54b](https://github.com/PurrNet/PurrNet/commit/de0c54b1f1c33ab3c7fac9d607b2d97bf83699d0))
* Awaitable error on older versions ([7cd6ad9](https://github.com/PurrNet/PurrNet/commit/7cd6ad923ecfd4af2658728b2d0b337d8902b380))
* base writing replace old pointer ([23573a4](https://github.com/PurrNet/PurrNet/commit/23573a43b0e024f68c6694d323b33c7f07694bdf))
* Better button placement ([26d2e12](https://github.com/PurrNet/PurrNet/commit/26d2e12f883553d39ad82c7721bc4b6cf6541af1))
* better cancellation for purrtransport ([e7bbc5f](https://github.com/PurrNet/PurrNet/commit/e7bbc5f8b105edc0e81564900922f21858ebc6f2))
* better interface checking ([45cce33](https://github.com/PurrNet/PurrNet/commit/45cce33dd652e46113c1670911767211b720ea0c))
* Bitpacker updated for improved class handling ([9c42b92](https://github.com/PurrNet/PurrNet/commit/9c42b926e5404b7b5ed453fe31052a4467923549))
* BREAKING CHANGE fixed type in `AuthenticationBehaviour<T>`, renamed `GetClientPlayload` to `GetClientPayload` ([b03e333](https://github.com/PurrNet/PurrNet/commit/b03e333c40c3e637b67806041136c29df4ff3276))
* cleanup can run into destroyed identities ([539dd76](https://github.com/PurrNet/PurrNet/commit/539dd768b28e26c6db09ef676dd40e543ea66e62))
* Collider3DExtensions for other casting methods ([dfeac1f](https://github.com/PurrNet/PurrNet/commit/dfeac1f088ce9fb1f79d18d58fb43472fa2801d4))
* Compare synclist delta when receiving full state ([24aca2f](https://github.com/PurrNet/PurrNet/commit/24aca2f5efa6f3c4595e9baed3effe3561a5bc6f))
* Correct push ([a2bbc9b](https://github.com/PurrNet/PurrNet/commit/a2bbc9baa8c852c6bd1492df12c9f45e012da8f5))
* custom dela packer for NetworkID? is obsolete now ([353082c](https://github.com/PurrNet/PurrNet/commit/353082cbf300138b5f6d30dfd14d741e93fe3ab1))
* disposable list leak detection and GC reduction ([02be3c5](https://github.com/PurrNet/PurrNet/commit/02be3c5e8508d8eca16297f9288f9005ec3f8edc))
* disposing stuff ([6b74e68](https://github.com/PurrNet/PurrNet/commit/6b74e68801f1ca3667c26504b893482c82c35b63))
* dont use System.Threading.Tasks.Task.Yield due to webgl ([8c358bb](https://github.com/PurrNet/PurrNet/commit/8c358bb4739aa546a859cf28803553c2070329fb))
* Extended SyncVar callback to also include old value ([ffee19e](https://github.com/PurrNet/PurrNet/commit/ffee19ec610fb645ff97608bd718d9f854aa6267))
* for steam if localhost or local ip just connect to self ([43e9019](https://github.com/PurrNet/PurrNet/commit/43e9019e03e8efa916dc96abaa6d60c0b3fcbb3b))
* if parent type doesn't have a writer, use the specified type one ([e8df49a](https://github.com/PurrNet/PurrNet/commit/e8df49a1296e2082c3368d7fc60d4ccc1d026f2a))
* Improved purr buttons to work with inheritance ([d7363bb](https://github.com/PurrNet/PurrNet/commit/d7363bb889d5b75bc99d18ee75ec507f158becce))
* include Cache-Control header too ([86badfa](https://github.com/PurrNet/PurrNet/commit/86badfac77a023d7ca67aad322816fdca0ca0f70))
* include purrnet version and color buttons insteasd of showing LEDs ([9612890](https://github.com/PurrNet/PurrNet/commit/9612890fbee45da9f795ef4574894c25f9dcbefe))
* introduce `SetDirty` for syncvars ([dcd8f86](https://github.com/PurrNet/PurrNet/commit/dcd8f86d22a451d4128b5d3b5661e9a19e568c04))
* introduce LateLateUpdate for nt ([86c3d87](https://github.com/PurrNet/PurrNet/commit/86c3d87e49fce11e572261df6cbd6c22c8ec06d2))
* leak checker; removing some GC for rpcs ([3578dcf](https://github.com/PurrNet/PurrNet/commit/3578dcf1e6faee1a5c3eca086f406b15065fa98a))
* make sure client has the isSpawned boolean set to true ([568e256](https://github.com/PurrNet/PurrNet/commit/568e2563be49450e2339bfd61b7f10fd25cde4f4))
* make sure to apply the changed value ([83822be](https://github.com/PurrNet/PurrNet/commit/83822be32cef9a66ff712268291734ad2030e2d9))
* make sure we don't create something that is already registered ([78a6907](https://github.com/PurrNet/PurrNet/commit/78a69075603bf4248681989dbeba00edd0176898))
* make syncvar change existing value instead of creating a new one ([e9a7336](https://github.com/PurrNet/PurrNet/commit/e9a7336e1d8ecdb36c2ba420113158ee20eeb9eb))
* more purr transport tweaks ([a6da989](https://github.com/PurrNet/PurrNet/commit/a6da9895d9f511fb00566d4afaaa0cadbb562498))
* more raycast types for rollback module ([975ab10](https://github.com/PurrNet/PurrNet/commit/975ab103da67a36097f36517ec6255e96f9f6a83))
* move retry logic to purrtransport api level ([5d209a8](https://github.com/PurrNet/PurrNet/commit/5d209a8942838cbc797a3fa6e0bb85baaefc2759))
* Network assets post asset processing proper push ([b383377](https://github.com/PurrNet/PurrNet/commit/b3833779d77bbc2ab3b23e78960c7cebd53db359))
* NetworkAssetsEditor and null assets ([c30cc95](https://github.com/PurrNet/PurrNet/commit/c30cc95decee22b1cbd4825b77584b55725ece1a))
* Packer handling of unspawned gameobjects and transforms ([cc68315](https://github.com/PurrNet/PurrNet/commit/cc6831536deabda40ee8f7cce69d204692ab78fb))
* packer rework ([9630787](https://github.com/PurrNet/PurrNet/commit/9630787b9ba57066fd59cf84673d777d2ef756db))
* populate local player id as soon as server has it ([7fddf9d](https://github.com/PurrNet/PurrNet/commit/7fddf9dde5de0b03edd729ce3fb021b97c69567d))
* push `IsRegistered` ([b72a193](https://github.com/PurrNet/PurrNet/commit/b72a1931cf3cbe922a058c0bfd41cb4a58cae197))
* Quick stupid fix ([8804efe](https://github.com/PurrNet/PurrNet/commit/8804efed49cc42de997b7dc66f2923d64dde4bd1))
* remove readonly from ApplyTo method ([b3a0d13](https://github.com/PurrNet/PurrNet/commit/b3a0d131c731061a3c284caeb76ca03b4384fe8e))
* rename rollback methods and further test them ([5f10efd](https://github.com/PurrNet/PurrNet/commit/5f10efd7fa8f4e2ce3694cc755d4e03202bd69b1))
* retry for purr transport if first fails ([8330de0](https://github.com/PurrNet/PurrNet/commit/8330de02f989757d0d10c6855dce717c3166a90c))
* Scene objects spawn issue for HOST ([6cf0b02](https://github.com/PurrNet/PurrNet/commit/6cf0b0209b02fb50f20e5d2f1f926f5d99c56a15))
* simplify generic logic ([2a48bf3](https://github.com/PurrNet/PurrNet/commit/2a48bf37b8af52891d69508af835e46d29951dee))
* skip deep processing of certain assemblies ([6fe1411](https://github.com/PurrNet/PurrNet/commit/6fe1411d39b54221f168a80f26b335e9e5153063))
* some missed cases for dispose here ([1e751ee](https://github.com/PurrNet/PurrNet/commit/1e751ee8c278f6b936fa5ef713027c4ccd817d14))
* some serialization intricacies ([d8973f9](https://github.com/PurrNet/PurrNet/commit/d8973f9d0833793bb153c0fe69cd634c2c0c00e4))
* stopping steam server didn't properly close existing client connections ([ea36cb5](https://github.com/PurrNet/PurrNet/commit/ea36cb5e883ab159fd2866ab5f12c4ca8638a84f))
* syncvar let client decide instead of server for ownerauth stuff ([5b4cb65](https://github.com/PurrNet/PurrNet/commit/5b4cb65e423378f28e6e228832a5e2d3a18ea73a))
* trigger OnEarlySpawn when catching up ([9443c97](https://github.com/PurrNet/PurrNet/commit/9443c97afb637a497bbbc3e0ed11b8d1993f2f73))
* try to be more careful with errors here ([3beb8d5](https://github.com/PurrNet/PurrNet/commit/3beb8d548a90d3ab5f2d9b3d7644f7eeacaaa624))
* tuples were breaking code stripping ([2ec1406](https://github.com/PurrNet/PurrNet/commit/2ec14060bafb072877facca8b3949d475d292f1c))
* undo early client id setting as it was incorrect ([285268b](https://github.com/PurrNet/PurrNet/commit/285268b7390c2d9c9affe09dd04180f4b1fcb3b2))
* undo serialization order of base type ([d8c8560](https://github.com/PurrNet/PurrNet/commit/d8c85601e8f5f886a24d999e453c1c8bc5732e3f))
* use unscaledDeltaTime for NetworkTransform.cs ([77c23c9](https://github.com/PurrNet/PurrNet/commit/77c23c9bfc3279c435cc665e4db9f4bd2fae9172))
* webgl builds ([4dccfa5](https://github.com/PurrNet/PurrNet/commit/4dccfa56f567a24f881b14585082a0eb29113bc7))
* when adding connection make sure it's a new ID ([a61f451](https://github.com/PurrNet/PurrNet/commit/a61f4511b519e0d65af8e57b63973358c92e3bfd))


### Continuous Integration

* **release:** 1.13.0-beta.31 [skip ci] ([b1a396c](https://github.com/PurrNet/PurrNet/commit/b1a396c72e2313680f9adbc7ca46add33be67282))


### Features

* add toolbar display settings ([f289470](https://github.com/PurrNet/PurrNet/commit/f289470cb3f40623bc434c16afd79b4fc9cd98a7))
* client/server purrnet version missmatch checker ([3387274](https://github.com/PurrNet/PurrNet/commit/3387274f24a8e1e9a33aaf502d3e81afc6d35b4d))
* Copy my SteamID to clipboard ([8d504e4](https://github.com/PurrNet/PurrNet/commit/8d504e43a9df6d5c5da622b61457362d7730782a))
* Enable Pool Debug menu item ([c53c455](https://github.com/PurrNet/PurrNet/commit/c53c455b5265b74fd1a46e0975e1b505d7457b10))
* introduce `RawNetManager` ([59aa743](https://github.com/PurrNet/PurrNet/commit/59aa743f1f366431135b0846ceb8c63ddbad4937))
* introduce api to HierarchyV2 module that allows to manually manage spawning and observability events for lower level control ([9825580](https://github.com/PurrNet/PurrNet/commit/982558000c56142ed472b205e38a6a96e4aff96e))
* spawn validator for client spawning ([569ef7a](https://github.com/PurrNet/PurrNet/commit/569ef7a38a6b136f13d725ac993162d547e51e51))


### BREAKING CHANGES

* **release:** fixed type in `AuthenticationBehaviour<T>`, renamed `GetClientPlayload` to `GetClientPayload` ([b03e333](https://github.com/PurrNet/PurrNet/commit/b03e333c40c3e637b67806041136c29df4ff3276))

# [1.13.0-beta.62](https://github.com/PurrNet/PurrNet/compare/v1.13.0-beta.61...v1.13.0-beta.62) (2025-07-31)


### Bug Fixes

* disposing stuff ([6b74e68](https://github.com/PurrNet/PurrNet/commit/6b74e68801f1ca3667c26504b893482c82c35b63))

# [1.13.0-beta.61](https://github.com/PurrNet/PurrNet/compare/v1.13.0-beta.60...v1.13.0-beta.61) (2025-07-31)


### Bug Fixes

* push `IsRegistered` ([b72a193](https://github.com/PurrNet/PurrNet/commit/b72a1931cf3cbe922a058c0bfd41cb4a58cae197))

# [1.13.0-beta.60](https://github.com/PurrNet/PurrNet/compare/v1.13.0-beta.59...v1.13.0-beta.60) (2025-07-31)


### Bug Fixes

* if parent type doesn't have a writer, use the specified type one ([e8df49a](https://github.com/PurrNet/PurrNet/commit/e8df49a1296e2082c3368d7fc60d4ccc1d026f2a))

# [1.13.0-beta.59](https://github.com/PurrNet/PurrNet/compare/v1.13.0-beta.58...v1.13.0-beta.59) (2025-07-31)


### Features

* Enable Pool Debug menu item ([c53c455](https://github.com/PurrNet/PurrNet/commit/c53c455b5265b74fd1a46e0975e1b505d7457b10))

# [1.13.0-beta.58](https://github.com/PurrNet/PurrNet/compare/v1.13.0-beta.57...v1.13.0-beta.58) (2025-07-31)


### Features

* client/server purrnet version missmatch checker ([3387274](https://github.com/PurrNet/PurrNet/commit/3387274f24a8e1e9a33aaf502d3e81afc6d35b4d))

# [1.13.0-beta.57](https://github.com/PurrNet/PurrNet/compare/v1.13.0-beta.56...v1.13.0-beta.57) (2025-07-30)


### Bug Fixes

* some missed cases for dispose here ([1e751ee](https://github.com/PurrNet/PurrNet/commit/1e751ee8c278f6b936fa5ef713027c4ccd817d14))

# [1.13.0-beta.56](https://github.com/PurrNet/PurrNet/compare/v1.13.0-beta.55...v1.13.0-beta.56) (2025-07-30)


### Bug Fixes

* base writing replace old pointer ([23573a4](https://github.com/PurrNet/PurrNet/commit/23573a43b0e024f68c6694d323b33c7f07694bdf))

# [1.13.0-beta.55](https://github.com/PurrNet/PurrNet/compare/v1.13.0-beta.54...v1.13.0-beta.55) (2025-07-30)


### Bug Fixes

* undo serialization order of base type ([d8c8560](https://github.com/PurrNet/PurrNet/commit/d8c85601e8f5f886a24d999e453c1c8bc5732e3f))

# [1.13.0-beta.54](https://github.com/PurrNet/PurrNet/compare/v1.13.0-beta.53...v1.13.0-beta.54) (2025-07-30)


### Bug Fixes

* some serialization intricacies ([d8973f9](https://github.com/PurrNet/PurrNet/commit/d8973f9d0833793bb153c0fe69cd634c2c0c00e4))

# [1.13.0-beta.53](https://github.com/PurrNet/PurrNet/compare/v1.13.0-beta.52...v1.13.0-beta.53) (2025-07-30)


### Bug Fixes

* disposable list leak detection and GC reduction ([02be3c5](https://github.com/PurrNet/PurrNet/commit/02be3c5e8508d8eca16297f9288f9005ec3f8edc))

# [1.13.0-beta.52](https://github.com/PurrNet/PurrNet/compare/v1.13.0-beta.51...v1.13.0-beta.52) (2025-07-30)


### Bug Fixes

* leak checker; removing some GC for rpcs ([3578dcf](https://github.com/PurrNet/PurrNet/commit/3578dcf1e6faee1a5c3eca086f406b15065fa98a))

# [1.13.0-beta.51](https://github.com/PurrNet/PurrNet/compare/v1.13.0-beta.50...v1.13.0-beta.51) (2025-07-30)


### Bug Fixes

* make syncvar change existing value instead of creating a new one ([e9a7336](https://github.com/PurrNet/PurrNet/commit/e9a7336e1d8ecdb36c2ba420113158ee20eeb9eb))

# [1.13.0-beta.50](https://github.com/PurrNet/PurrNet/compare/v1.13.0-beta.49...v1.13.0-beta.50) (2025-07-30)


### Bug Fixes

* introduce `SetDirty` for syncvars ([dcd8f86](https://github.com/PurrNet/PurrNet/commit/dcd8f86d22a451d4128b5d3b5661e9a19e568c04))

# [1.13.0-beta.49](https://github.com/PurrNet/PurrNet/compare/v1.13.0-beta.48...v1.13.0-beta.49) (2025-07-28)


### Bug Fixes

* Added Disposable HashSet creation ([9f1b2e3](https://github.com/PurrNet/PurrNet/commit/9f1b2e3dfdf30cc56960b631dd56147ae7563671))

# [1.13.0-beta.48](https://github.com/PurrNet/PurrNet/compare/v1.13.0-beta.47...v1.13.0-beta.48) (2025-07-27)


### Bug Fixes

* use unscaledDeltaTime for NetworkTransform.cs ([77c23c9](https://github.com/PurrNet/PurrNet/commit/77c23c9bfc3279c435cc665e4db9f4bd2fae9172))

# [1.13.0-beta.47](https://github.com/PurrNet/PurrNet/compare/v1.13.0-beta.46...v1.13.0-beta.47) (2025-07-27)


### Bug Fixes

* better interface checking ([45cce33](https://github.com/PurrNet/PurrNet/commit/45cce33dd652e46113c1670911767211b720ea0c))
* make sure to apply the changed value ([83822be](https://github.com/PurrNet/PurrNet/commit/83822be32cef9a66ff712268291734ad2030e2d9))
* packer rework ([9630787](https://github.com/PurrNet/PurrNet/commit/9630787b9ba57066fd59cf84673d777d2ef756db))
* simplify generic logic ([2a48bf3](https://github.com/PurrNet/PurrNet/commit/2a48bf37b8af52891d69508af835e46d29951dee))

# [1.13.0-beta.46](https://github.com/PurrNet/PurrNet/compare/v1.13.0-beta.45...v1.13.0-beta.46) (2025-07-27)


### Bug Fixes

* Bitpacker updated for improved class handling ([9c42b92](https://github.com/PurrNet/PurrNet/commit/9c42b926e5404b7b5ed453fe31052a4467923549))
* Compare synclist delta when receiving full state ([24aca2f](https://github.com/PurrNet/PurrNet/commit/24aca2f5efa6f3c4595e9baed3effe3561a5bc6f))

# [1.13.0-beta.45](https://github.com/PurrNet/PurrNet/compare/v1.13.0-beta.44...v1.13.0-beta.45) (2025-07-25)


### Bug Fixes

* Additional safety added to packer of gameobject and transform ([20f3623](https://github.com/PurrNet/PurrNet/commit/20f36236b4d6929a8bf1956ae52175ce09ad7824))

# [1.13.0-beta.44](https://github.com/PurrNet/PurrNet/compare/v1.13.0-beta.43...v1.13.0-beta.44) (2025-07-25)


### Bug Fixes

* Packer handling of unspawned gameobjects and transforms ([cc68315](https://github.com/PurrNet/PurrNet/commit/cc6831536deabda40ee8f7cce69d204692ab78fb))

# [1.13.0-beta.43](https://github.com/PurrNet/PurrNet/compare/v1.13.0-beta.42...v1.13.0-beta.43) (2025-07-24)


### Features

* introduce `RawNetManager` ([59aa743](https://github.com/PurrNet/PurrNet/commit/59aa743f1f366431135b0846ceb8c63ddbad4937))

# [1.13.0-beta.42](https://github.com/PurrNet/PurrNet/compare/v1.13.0-beta.41...v1.13.0-beta.42) (2025-07-23)


### Bug Fixes

* remove readonly from ApplyTo method ([b3a0d13](https://github.com/PurrNet/PurrNet/commit/b3a0d131c731061a3c284caeb76ca03b4384fe8e))

# [1.13.0-beta.41](https://github.com/PurrNet/PurrNet/compare/v1.13.0-beta.40...v1.13.0-beta.41) (2025-07-22)


### Bug Fixes

* include Cache-Control header too ([86badfa](https://github.com/PurrNet/PurrNet/commit/86badfac77a023d7ca67aad322816fdca0ca0f70))

# [1.13.0-beta.40](https://github.com/PurrNet/PurrNet/compare/v1.13.0-beta.39...v1.13.0-beta.40) (2025-07-22)


### Bug Fixes

* attempt to circumvent caching ([de0c54b](https://github.com/PurrNet/PurrNet/commit/de0c54b1f1c33ab3c7fac9d607b2d97bf83699d0))

# [1.13.0-beta.39](https://github.com/PurrNet/PurrNet/compare/v1.13.0-beta.38...v1.13.0-beta.39) (2025-07-22)


### Bug Fixes

* more purr transport tweaks ([a6da989](https://github.com/PurrNet/PurrNet/commit/a6da9895d9f511fb00566d4afaaa0cadbb562498))

# [1.13.0-beta.38](https://github.com/PurrNet/PurrNet/compare/v1.13.0-beta.37...v1.13.0-beta.38) (2025-07-22)


### Bug Fixes

* better cancellation for purrtransport ([e7bbc5f](https://github.com/PurrNet/PurrNet/commit/e7bbc5f8b105edc0e81564900922f21858ebc6f2))

# [1.13.0-beta.37](https://github.com/PurrNet/PurrNet/compare/v1.13.0-beta.36...v1.13.0-beta.37) (2025-07-22)


### Bug Fixes

* move retry logic to purrtransport api level ([5d209a8](https://github.com/PurrNet/PurrNet/commit/5d209a8942838cbc797a3fa6e0bb85baaefc2759))

# [1.13.0-beta.36](https://github.com/PurrNet/PurrNet/compare/v1.13.0-beta.35...v1.13.0-beta.36) (2025-07-22)


### Bug Fixes

* webgl builds ([4dccfa5](https://github.com/PurrNet/PurrNet/commit/4dccfa56f567a24f881b14585082a0eb29113bc7))

# [1.13.0-beta.35](https://github.com/PurrNet/PurrNet/compare/v1.13.0-beta.34...v1.13.0-beta.35) (2025-07-21)


### Bug Fixes

* attempt at fixing steam issue ([6c96b84](https://github.com/PurrNet/PurrNet/commit/6c96b8432ce3b6a94f29da7d01f06110bea646b4))

# [1.13.0-beta.34](https://github.com/PurrNet/PurrNet/compare/v1.13.0-beta.33...v1.13.0-beta.34) (2025-07-21)


### Bug Fixes

* for steam if localhost or local ip just connect to self ([43e9019](https://github.com/PurrNet/PurrNet/commit/43e9019e03e8efa916dc96abaa6d60c0b3fcbb3b))

# [1.13.0-beta.33](https://github.com/PurrNet/PurrNet/compare/v1.13.0-beta.32...v1.13.0-beta.33) (2025-07-21)


### Features

* Copy my SteamID to clipboard ([8d504e4](https://github.com/PurrNet/PurrNet/commit/8d504e43a9df6d5c5da622b61457362d7730782a))

# [1.13.0-beta.32](https://github.com/PurrNet/PurrNet/compare/v1.13.0-beta.31...v1.13.0-beta.32) (2025-07-21)


### Bug Fixes

* retry for purr transport if first fails ([8330de0](https://github.com/PurrNet/PurrNet/commit/8330de02f989757d0d10c6855dce717c3166a90c))

# [1.13.0-beta.31](https://github.com/PurrNet/PurrNet/compare/v1.13.0-beta.30...v1.13.0-beta.31) (2025-07-21)


### Bug Fixes

* BREAKING CHANGE fixed type in `AuthenticationBehaviour<T>`, renamed `GetClientPlayload` to `GetClientPayload` ([b03e333](https://github.com/PurrNet/PurrNet/commit/b03e333c40c3e637b67806041136c29df4ff3276))

# [1.13.0-beta.30](https://github.com/PurrNet/PurrNet/compare/v1.13.0-beta.29...v1.13.0-beta.30) (2025-07-21)


### Bug Fixes

* undo early client id setting as it was incorrect ([285268b](https://github.com/PurrNet/PurrNet/commit/285268b7390c2d9c9affe09dd04180f4b1fcb3b2))

# [1.13.0-beta.29](https://github.com/PurrNet/PurrNet/compare/v1.13.0-beta.28...v1.13.0-beta.29) (2025-07-19)


### Bug Fixes

* Better button placement ([26d2e12](https://github.com/PurrNet/PurrNet/commit/26d2e12f883553d39ad82c7721bc4b6cf6541af1))

# [1.13.0-beta.28](https://github.com/PurrNet/PurrNet/compare/v1.13.0-beta.27...v1.13.0-beta.28) (2025-07-19)


### Bug Fixes

* Added connection UI example ([36be104](https://github.com/PurrNet/PurrNet/commit/36be104386c6237c5c6222a33b362906f30b4f32))

# [1.13.0-beta.27](https://github.com/PurrNet/PurrNet/compare/v1.13.0-beta.26...v1.13.0-beta.27) (2025-07-17)


### Bug Fixes

* make sure client has the isSpawned boolean set to true ([568e256](https://github.com/PurrNet/PurrNet/commit/568e2563be49450e2339bfd61b7f10fd25cde4f4))

# [1.13.0-beta.26](https://github.com/PurrNet/PurrNet/compare/v1.13.0-beta.25...v1.13.0-beta.26) (2025-07-17)


### Bug Fixes

* populate local player id as soon as server has it ([7fddf9d](https://github.com/PurrNet/PurrNet/commit/7fddf9dde5de0b03edd729ce3fb021b97c69567d))

# [1.13.0-beta.25](https://github.com/PurrNet/PurrNet/compare/v1.13.0-beta.24...v1.13.0-beta.25) (2025-07-17)


### Bug Fixes

* `isIk` wasn't checking enough cases thx @OverGast ([4dd1c01](https://github.com/PurrNet/PurrNet/commit/4dd1c0133428365101c3bb28f087c9345ae0cc1e))

# [1.13.0-beta.24](https://github.com/PurrNet/PurrNet/compare/v1.13.0-beta.23...v1.13.0-beta.24) (2025-07-17)


### Bug Fixes

* skip deep processing of certain assemblies ([6fe1411](https://github.com/PurrNet/PurrNet/commit/6fe1411d39b54221f168a80f26b335e9e5153063))

# [1.13.0-beta.23](https://github.com/PurrNet/PurrNet/compare/v1.13.0-beta.22...v1.13.0-beta.23) (2025-07-17)


### Bug Fixes

* also render purrnet toolbar on clones ([c3dbb1c](https://github.com/PurrNet/PurrNet/commit/c3dbb1cd127403cba11f5a9c415a82e6679b38e4))

# [1.13.0-beta.22](https://github.com/PurrNet/PurrNet/compare/v1.13.0-beta.21...v1.13.0-beta.22) (2025-07-17)


### Features

* add toolbar display settings ([f289470](https://github.com/PurrNet/PurrNet/commit/f289470cb3f40623bc434c16afd79b4fc9cd98a7))

# [1.13.0-beta.21](https://github.com/PurrNet/PurrNet/compare/v1.13.0-beta.20...v1.13.0-beta.21) (2025-07-16)


### Bug Fixes

* NetworkAssetsEditor and null assets ([c30cc95](https://github.com/PurrNet/PurrNet/commit/c30cc95decee22b1cbd4825b77584b55725ece1a))

# [1.13.0-beta.20](https://github.com/PurrNet/PurrNet/compare/v1.13.0-beta.19...v1.13.0-beta.20) (2025-07-16)


### Bug Fixes

* include purrnet version and color buttons insteasd of showing LEDs ([9612890](https://github.com/PurrNet/PurrNet/commit/9612890fbee45da9f795ef4574894c25f9dcbefe))

# [1.13.0-beta.19](https://github.com/PurrNet/PurrNet/compare/v1.13.0-beta.18...v1.13.0-beta.19) (2025-07-16)


### Bug Fixes

* Awaitable error on older versions ([7cd6ad9](https://github.com/PurrNet/PurrNet/commit/7cd6ad923ecfd4af2658728b2d0b337d8902b380))

# [1.13.0-beta.18](https://github.com/PurrNet/PurrNet/compare/v1.13.0-beta.17...v1.13.0-beta.18) (2025-07-15)


### Bug Fixes

* Improved purr buttons to work with inheritance ([d7363bb](https://github.com/PurrNet/PurrNet/commit/d7363bb889d5b75bc99d18ee75ec507f158becce))

# [1.13.0-beta.17](https://github.com/PurrNet/PurrNet/compare/v1.13.0-beta.16...v1.13.0-beta.17) (2025-07-14)


### Bug Fixes

* Extended SyncVar callback to also include old value ([ffee19e](https://github.com/PurrNet/PurrNet/commit/ffee19ec610fb645ff97608bd718d9f854aa6267))

# [1.13.0-beta.16](https://github.com/PurrNet/PurrNet/compare/v1.13.0-beta.15...v1.13.0-beta.16) (2025-07-13)


### Bug Fixes

* syncvar let client decide instead of server for ownerauth stuff ([5b4cb65](https://github.com/PurrNet/PurrNet/commit/5b4cb65e423378f28e6e228832a5e2d3a18ea73a))

# [1.13.0-beta.15](https://github.com/PurrNet/PurrNet/compare/v1.13.0-beta.14...v1.13.0-beta.15) (2025-07-13)


### Bug Fixes

* Scene objects spawn issue for HOST ([6cf0b02](https://github.com/PurrNet/PurrNet/commit/6cf0b0209b02fb50f20e5d2f1f926f5d99c56a15))

# [1.13.0-beta.14](https://github.com/PurrNet/PurrNet/compare/v1.13.0-beta.13...v1.13.0-beta.14) (2025-07-12)


### Bug Fixes

* Correct push ([a2bbc9b](https://github.com/PurrNet/PurrNet/commit/a2bbc9baa8c852c6bd1492df12c9f45e012da8f5))
* Quick stupid fix ([8804efe](https://github.com/PurrNet/PurrNet/commit/8804efed49cc42de997b7dc66f2923d64dde4bd1))

# [1.13.0-beta.13](https://github.com/PurrNet/PurrNet/compare/v1.13.0-beta.12...v1.13.0-beta.13) (2025-07-12)


### Bug Fixes

* Added create overloads for disposable types ([6650be1](https://github.com/PurrNet/PurrNet/commit/6650be1301666f0c87fa68d4a0bc6c7f0d9fcb4e))

# [1.13.0-beta.12](https://github.com/PurrNet/PurrNet/compare/v1.13.0-beta.11...v1.13.0-beta.12) (2025-07-12)


### Bug Fixes

* tuples were breaking code stripping ([2ec1406](https://github.com/PurrNet/PurrNet/commit/2ec14060bafb072877facca8b3949d475d292f1c))

# [1.13.0-beta.11](https://github.com/PurrNet/PurrNet/compare/v1.13.0-beta.10...v1.13.0-beta.11) (2025-07-11)


### Bug Fixes

* Network assets post asset processing proper push ([b383377](https://github.com/PurrNet/PurrNet/commit/b3833779d77bbc2ab3b23e78960c7cebd53db359))

# [1.13.0-beta.10](https://github.com/PurrNet/PurrNet/compare/v1.13.0-beta.9...v1.13.0-beta.10) (2025-07-11)


### Bug Fixes

* Added proper asset post processing to network assets ([c10f7fe](https://github.com/PurrNet/PurrNet/commit/c10f7feade5ad191f5fea8b1d1a3f2d32a3f64ef))

# [1.13.0-beta.9](https://github.com/PurrNet/PurrNet/compare/v1.13.0-beta.8...v1.13.0-beta.9) (2025-07-11)


### Bug Fixes

* when adding connection make sure it's a new ID ([a61f451](https://github.com/PurrNet/PurrNet/commit/a61f4511b519e0d65af8e57b63973358c92e3bfd))

# [1.13.0-beta.8](https://github.com/PurrNet/PurrNet/compare/v1.13.0-beta.7...v1.13.0-beta.8) (2025-07-10)


### Bug Fixes

* custom dela packer for NetworkID? is obsolete now ([353082c](https://github.com/PurrNet/PurrNet/commit/353082cbf300138b5f6d30dfd14d741e93fe3ab1))
* make sure we don't create something that is already registered ([78a6907](https://github.com/PurrNet/PurrNet/commit/78a69075603bf4248681989dbeba00edd0176898))

# [1.13.0-beta.7](https://github.com/PurrNet/PurrNet/compare/v1.13.0-beta.6...v1.13.0-beta.7) (2025-07-10)


### Bug Fixes

* cleanup can run into destroyed identities ([539dd76](https://github.com/PurrNet/PurrNet/commit/539dd768b28e26c6db09ef676dd40e543ea66e62))

# [1.13.0-beta.6](https://github.com/PurrNet/PurrNet/compare/v1.13.0-beta.5...v1.13.0-beta.6) (2025-07-10)


### Bug Fixes

* allow for manual despawning too ([0a01be8](https://github.com/PurrNet/PurrNet/commit/0a01be8322a43085da4a0e8b9a9f22de16033ee5))

# [1.13.0-beta.5](https://github.com/PurrNet/PurrNet/compare/v1.13.0-beta.4...v1.13.0-beta.5) (2025-07-10)


### Features

* introduce api to HierarchyV2 module that allows to manually manage spawning and observability events for lower level control ([9825580](https://github.com/PurrNet/PurrNet/commit/982558000c56142ed472b205e38a6a96e4aff96e))

# [1.13.0-beta.4](https://github.com/PurrNet/PurrNet/compare/v1.13.0-beta.3...v1.13.0-beta.4) (2025-07-10)


### Bug Fixes

* stopping steam server didn't properly close existing client connections ([ea36cb5](https://github.com/PurrNet/PurrNet/commit/ea36cb5e883ab159fd2866ab5f12c4ca8638a84f))

# [1.13.0-beta.3](https://github.com/PurrNet/PurrNet/compare/v1.13.0-beta.2...v1.13.0-beta.3) (2025-07-09)


### Bug Fixes

* try to be more careful with errors here ([3beb8d5](https://github.com/PurrNet/PurrNet/commit/3beb8d548a90d3ab5f2d9b3d7644f7eeacaaa624))

# [1.13.0-beta.2](https://github.com/PurrNet/PurrNet/compare/v1.13.0-beta.1...v1.13.0-beta.2) (2025-07-08)


### Bug Fixes

* trigger OnEarlySpawn when catching up ([9443c97](https://github.com/PurrNet/PurrNet/commit/9443c97afb637a497bbbc3e0ed11b8d1993f2f73))

# [1.13.0-beta.1](https://github.com/PurrNet/PurrNet/compare/v1.12.5-beta.7...v1.13.0-beta.1) (2025-07-08)


### Features

* spawn validator for client spawning ([569ef7a](https://github.com/PurrNet/PurrNet/commit/569ef7a38a6b136f13d725ac993162d547e51e51))

## [1.12.5-beta.7](https://github.com/PurrNet/PurrNet/compare/v1.12.5-beta.6...v1.12.5-beta.7) (2025-07-08)


### Bug Fixes

* introduce LateLateUpdate for nt ([86c3d87](https://github.com/PurrNet/PurrNet/commit/86c3d87e49fce11e572261df6cbd6c22c8ec06d2))

## [1.12.5-beta.6](https://github.com/PurrNet/PurrNet/compare/v1.12.5-beta.5...v1.12.5-beta.6) (2025-07-08)


### Bug Fixes

* rename rollback methods and further test them ([5f10efd](https://github.com/PurrNet/PurrNet/commit/5f10efd7fa8f4e2ce3694cc755d4e03202bd69b1))

## [1.12.5-beta.5](https://github.com/PurrNet/PurrNet/compare/v1.12.5-beta.4...v1.12.5-beta.5) (2025-07-08)


### Bug Fixes

* more raycast types for rollback module ([975ab10](https://github.com/PurrNet/PurrNet/commit/975ab103da67a36097f36517ec6255e96f9f6a83))

## [1.12.5-beta.4](https://github.com/PurrNet/PurrNet/compare/v1.12.5-beta.3...v1.12.5-beta.4) (2025-07-07)


### Bug Fixes

* Collider3DExtensions for other casting methods ([dfeac1f](https://github.com/PurrNet/PurrNet/commit/dfeac1f088ce9fb1f79d18d58fb43472fa2801d4))

## [1.12.5-beta.3](https://github.com/PurrNet/PurrNet/compare/v1.12.5-beta.2...v1.12.5-beta.3) (2025-07-07)


### Bug Fixes

* add asServer for collider registration (rollback) ([92390cf](https://github.com/PurrNet/PurrNet/commit/92390cfda5053c55d005d28bd6c901ad6ee9af7b))

## [1.12.5-beta.2](https://github.com/PurrNet/PurrNet/compare/v1.12.5-beta.1...v1.12.5-beta.2) (2025-07-07)


### Bug Fixes

* allow to dynamically register colliders for rollback history ([2d2762b](https://github.com/PurrNet/PurrNet/commit/2d2762b493afcc604c9e7e3e7fa3a24c50c42125))

## [1.12.5-beta.1](https://github.com/PurrNet/PurrNet/compare/v1.12.4...v1.12.5-beta.1) (2025-07-07)


### Bug Fixes

* dont use System.Threading.Tasks.Task.Yield due to webgl ([8c358bb](https://github.com/PurrNet/PurrNet/commit/8c358bb4739aa546a859cf28803553c2070329fb))

## [1.12.4](https://github.com/PurrNet/PurrNet/compare/v1.12.3...v1.12.4) (2025-07-07)


### Bug Fixes

* Added disposable list static creation ([101cf00](https://github.com/PurrNet/PurrNet/commit/101cf009c2bf5157a4e6cdeba973d81c0e4b54f7))
* better fallback serializers for delta compression ([2276832](https://github.com/PurrNet/PurrNet/commit/2276832f3f2ade89177e0550663aa4964361cd67))
* delta packer for generic System.Object ([479b535](https://github.com/PurrNet/PurrNet/commit/479b5356a4e01273f638c4c38f8b2f5e3ebfe0db))
* diposable dic packer stuff again ([707fcb8](https://github.com/PurrNet/PurrNet/commit/707fcb86a080c0de3c07a934288b1bf140ae76db))
* disposable dic delta writer ([73b7561](https://github.com/PurrNet/PurrNet/commit/73b75611d35929139324ca707e164e3e7588f3e0))
* fallback reader for delta didnt use new object serializer ([9541da6](https://github.com/PurrNet/PurrNet/commit/9541da68f9f5d96567578cf439c7be6d650ccbb8))
* hide in hierarchy only ([da99e58](https://github.com/PurrNet/PurrNet/commit/da99e58c17221bef61364cb9940159cdf06512c7))
* introduce the `Create(capacity)` variant for DisposableList ([4d1fab3](https://github.com/PurrNet/PurrNet/commit/4d1fab33107353af379cae204924f2c59795bdf7))
* just dont process NuGetForUnity ([0140920](https://github.com/PurrNet/PurrNet/commit/0140920a19800fe4512210fdfb1f79e2660f35b3))
* more nuget tests ([a6d144d](https://github.com/PurrNet/PurrNet/commit/a6d144ddee1795ccc94d36fceb346746b956dfee))
* more test ([2b237cd](https://github.com/PurrNet/PurrNet/commit/2b237cde9c67e4f60b4c5415c11d8b811d331566))
* Network Asset also pull base class assets ([89b0d56](https://github.com/PurrNet/PurrNet/commit/89b0d567db0e02c35ff7d2a9e1b6a6705f584847))
* Network asset exclude editor namespace ([11b45f6](https://github.com/PurrNet/PurrNet/commit/11b45f67388ada773138a21c6e830a38cd20cf08))
* old value was wrong for dic delta packer ([539c760](https://github.com/PurrNet/PurrNet/commit/539c7607415c493a881e0d676c5f90d068cd41f8))
* possible fix for network reflection buld ([3bbf58e](https://github.com/PurrNet/PurrNet/commit/3bbf58e46da52d62add19f4fe10e78ad72052c85))
* revert ([f6ffe42](https://github.com/PurrNet/PurrNet/commit/f6ffe42e384224b925167df4f18c853cbd4c9bd3))
* rigidbody moving weirdly if pooled ([5cc8524](https://github.com/PurrNet/PurrNet/commit/5cc85245aabcb458a5b793eb6f1cde9b64424565))
* State machine double enter and exit fix ([1b5fbc8](https://github.com/PurrNet/PurrNet/commit/1b5fbc8b5a51ad6fa4ebf56711a8cd8b24b22cb5))
* trying to fix nuget package issues ([bbf83d6](https://github.com/PurrNet/PurrNet/commit/bbf83d699cb9c800dd709c97b560cbcaefd575b6))
* ulong delta packer ([01445ae](https://github.com/PurrNet/PurrNet/commit/01445ae5c0cd1ae2147337a6ee7d8eb90a4f51a0))

## [1.12.4-beta.20](https://github.com/PurrNet/PurrNet/compare/v1.12.4-beta.19...v1.12.4-beta.20) (2025-07-06)


### Bug Fixes

* State machine double enter and exit fix ([1b5fbc8](https://github.com/PurrNet/PurrNet/commit/1b5fbc8b5a51ad6fa4ebf56711a8cd8b24b22cb5))

## [1.12.4-beta.19](https://github.com/PurrNet/PurrNet/compare/v1.12.4-beta.18...v1.12.4-beta.19) (2025-07-03)


### Bug Fixes

* possible fix for network reflection buld ([3bbf58e](https://github.com/PurrNet/PurrNet/commit/3bbf58e46da52d62add19f4fe10e78ad72052c85))

## [1.12.4-beta.18](https://github.com/PurrNet/PurrNet/compare/v1.12.4-beta.17...v1.12.4-beta.18) (2025-07-03)


### Bug Fixes

* Network asset exclude editor namespace ([11b45f6](https://github.com/PurrNet/PurrNet/commit/11b45f67388ada773138a21c6e830a38cd20cf08))

## [1.12.4-beta.17](https://github.com/PurrNet/PurrNet/compare/v1.12.4-beta.16...v1.12.4-beta.17) (2025-07-03)


### Bug Fixes

* Network Asset also pull base class assets ([89b0d56](https://github.com/PurrNet/PurrNet/commit/89b0d567db0e02c35ff7d2a9e1b6a6705f584847))

## [1.12.4-beta.16](https://github.com/PurrNet/PurrNet/compare/v1.12.4-beta.15...v1.12.4-beta.16) (2025-07-01)


### Bug Fixes

* introduce the `Create(capacity)` variant for DisposableList ([4d1fab3](https://github.com/PurrNet/PurrNet/commit/4d1fab33107353af379cae204924f2c59795bdf7))

## [1.12.4-beta.15](https://github.com/PurrNet/PurrNet/compare/v1.12.4-beta.14...v1.12.4-beta.15) (2025-07-01)


### Bug Fixes

* rigidbody moving weirdly if pooled ([5cc8524](https://github.com/PurrNet/PurrNet/commit/5cc85245aabcb458a5b793eb6f1cde9b64424565))

## [1.12.4-beta.14](https://github.com/PurrNet/PurrNet/compare/v1.12.4-beta.13...v1.12.4-beta.14) (2025-07-01)


### Bug Fixes

* just dont process NuGetForUnity ([0140920](https://github.com/PurrNet/PurrNet/commit/0140920a19800fe4512210fdfb1f79e2660f35b3))

## [1.12.4-beta.13](https://github.com/PurrNet/PurrNet/compare/v1.12.4-beta.12...v1.12.4-beta.13) (2025-07-01)


### Bug Fixes

* revert ([f6ffe42](https://github.com/PurrNet/PurrNet/commit/f6ffe42e384224b925167df4f18c853cbd4c9bd3))

## [1.12.4-beta.12](https://github.com/PurrNet/PurrNet/compare/v1.12.4-beta.11...v1.12.4-beta.12) (2025-07-01)


### Bug Fixes

* more test ([2b237cd](https://github.com/PurrNet/PurrNet/commit/2b237cde9c67e4f60b4c5415c11d8b811d331566))

## [1.12.4-beta.11](https://github.com/PurrNet/PurrNet/compare/v1.12.4-beta.10...v1.12.4-beta.11) (2025-07-01)


### Bug Fixes

* more nuget tests ([a6d144d](https://github.com/PurrNet/PurrNet/commit/a6d144ddee1795ccc94d36fceb346746b956dfee))

## [1.12.4-beta.10](https://github.com/PurrNet/PurrNet/compare/v1.12.4-beta.9...v1.12.4-beta.10) (2025-07-01)


### Bug Fixes

* trying to fix nuget package issues ([bbf83d6](https://github.com/PurrNet/PurrNet/commit/bbf83d699cb9c800dd709c97b560cbcaefd575b6))

## [1.12.4-beta.9](https://github.com/PurrNet/PurrNet/compare/v1.12.4-beta.8...v1.12.4-beta.9) (2025-06-30)


### Bug Fixes

* fallback reader for delta didnt use new object serializer ([9541da6](https://github.com/PurrNet/PurrNet/commit/9541da68f9f5d96567578cf439c7be6d650ccbb8))

## [1.12.4-beta.8](https://github.com/PurrNet/PurrNet/compare/v1.12.4-beta.7...v1.12.4-beta.8) (2025-06-30)


### Bug Fixes

* Added disposable list static creation ([101cf00](https://github.com/PurrNet/PurrNet/commit/101cf009c2bf5157a4e6cdeba973d81c0e4b54f7))

## [1.12.4-beta.7](https://github.com/PurrNet/PurrNet/compare/v1.12.4-beta.6...v1.12.4-beta.7) (2025-06-30)


### Bug Fixes

* delta packer for generic System.Object ([479b535](https://github.com/PurrNet/PurrNet/commit/479b5356a4e01273f638c4c38f8b2f5e3ebfe0db))

## [1.12.4-beta.6](https://github.com/PurrNet/PurrNet/compare/v1.12.4-beta.5...v1.12.4-beta.6) (2025-06-30)


### Bug Fixes

* better fallback serializers for delta compression ([2276832](https://github.com/PurrNet/PurrNet/commit/2276832f3f2ade89177e0550663aa4964361cd67))

## [1.12.4-beta.5](https://github.com/PurrNet/PurrNet/compare/v1.12.4-beta.4...v1.12.4-beta.5) (2025-06-30)


### Bug Fixes

* ulong delta packer ([01445ae](https://github.com/PurrNet/PurrNet/commit/01445ae5c0cd1ae2147337a6ee7d8eb90a4f51a0))

## [1.12.4-beta.4](https://github.com/PurrNet/PurrNet/compare/v1.12.4-beta.3...v1.12.4-beta.4) (2025-06-30)


### Bug Fixes

* old value was wrong for dic delta packer ([539c760](https://github.com/PurrNet/PurrNet/commit/539c7607415c493a881e0d676c5f90d068cd41f8))

## [1.12.4-beta.3](https://github.com/PurrNet/PurrNet/compare/v1.12.4-beta.2...v1.12.4-beta.3) (2025-06-30)


### Bug Fixes

* diposable dic packer stuff again ([707fcb8](https://github.com/PurrNet/PurrNet/commit/707fcb86a080c0de3c07a934288b1bf140ae76db))

## [1.12.4-beta.2](https://github.com/PurrNet/PurrNet/compare/v1.12.4-beta.1...v1.12.4-beta.2) (2025-06-30)


### Bug Fixes

* disposable dic delta writer ([73b7561](https://github.com/PurrNet/PurrNet/commit/73b75611d35929139324ca707e164e3e7588f3e0))

## [1.12.4-beta.1](https://github.com/PurrNet/PurrNet/compare/v1.12.3...v1.12.4-beta.1) (2025-06-29)


### Bug Fixes

* hide in hierarchy only ([da99e58](https://github.com/PurrNet/PurrNet/commit/da99e58c17221bef61364cb9940159cdf06512c7))

## [1.12.3](https://github.com/PurrNet/PurrNet/compare/v1.12.2...v1.12.3) (2025-06-28)


### Bug Fixes

* add DisposableDictionary along side it's pool ([baa2c99](https://github.com/PurrNet/PurrNet/commit/baa2c9962539222ae62a203499712db0285321ce))
* always prepare the hash for `System.Object` ([5315b82](https://github.com/PurrNet/PurrNet/commit/5315b823ccb6edc1119b640c669b41538e711c8e))
* introduce disposable dictionary delta packers ([086c701](https://github.com/PurrNet/PurrNet/commit/086c701df10263ce47423aaf4b8aa20b023d8f51))
* ping calculations ([cd7cfd7](https://github.com/PurrNet/PurrNet/commit/cd7cfd70c1427c0d58dfe5e3601dd58ff79d2cb8))
* records ([983728a](https://github.com/PurrNet/PurrNet/commit/983728a6befc13b375ae4b8e5bbde8ed63c2cdbe))
* Server Stats added to statistics manager ([37a49ec](https://github.com/PurrNet/PurrNet/commit/37a49ec0279393a6a5330d6407f1f57fdc8d286c))
* Statistics for steam transport ([c1c16ff](https://github.com/PurrNet/PurrNet/commit/c1c16fff1692dd56c0db009e468ac87970d11adf))
* still prefer to call empty constructor instead of always initializing it to 0 ([5c667ac](https://github.com/PurrNet/PurrNet/commit/5c667ace880ba56b0a7b2aeb01066fcb60330fe0))
* whitelist dirty wasn't being executed ([7bb9351](https://github.com/PurrNet/PurrNet/commit/7bb93511c5afe551dfa5c73efa29aaad5161120c))
* writer for Ray2D ([24587cd](https://github.com/PurrNet/PurrNet/commit/24587cd0ee263e44e04997e3d09626300691f2e4))

## [1.12.3-beta.9](https://github.com/PurrNet/PurrNet/compare/v1.12.3-beta.8...v1.12.3-beta.9) (2025-06-28)


### Bug Fixes

* writer for Ray2D ([24587cd](https://github.com/PurrNet/PurrNet/commit/24587cd0ee263e44e04997e3d09626300691f2e4))

## [1.12.3-beta.8](https://github.com/PurrNet/PurrNet/compare/v1.12.3-beta.7...v1.12.3-beta.8) (2025-06-28)


### Bug Fixes

* always prepare the hash for `System.Object` ([5315b82](https://github.com/PurrNet/PurrNet/commit/5315b823ccb6edc1119b640c669b41538e711c8e))

## [1.12.3-beta.7](https://github.com/PurrNet/PurrNet/compare/v1.12.3-beta.6...v1.12.3-beta.7) (2025-06-28)


### Bug Fixes

* Server Stats added to statistics manager ([37a49ec](https://github.com/PurrNet/PurrNet/commit/37a49ec0279393a6a5330d6407f1f57fdc8d286c))

## [1.12.3-beta.6](https://github.com/PurrNet/PurrNet/compare/v1.12.3-beta.5...v1.12.3-beta.6) (2025-06-28)


### Bug Fixes

* introduce disposable dictionary delta packers ([086c701](https://github.com/PurrNet/PurrNet/commit/086c701df10263ce47423aaf4b8aa20b023d8f51))

## [1.12.3-beta.5](https://github.com/PurrNet/PurrNet/compare/v1.12.3-beta.4...v1.12.3-beta.5) (2025-06-28)


### Bug Fixes

* add DisposableDictionary along side it's pool ([baa2c99](https://github.com/PurrNet/PurrNet/commit/baa2c9962539222ae62a203499712db0285321ce))

## [1.12.3-beta.4](https://github.com/PurrNet/PurrNet/compare/v1.12.3-beta.3...v1.12.3-beta.4) (2025-06-27)


### Bug Fixes

* ping calculations ([cd7cfd7](https://github.com/PurrNet/PurrNet/commit/cd7cfd70c1427c0d58dfe5e3601dd58ff79d2cb8))
* Statistics for steam transport ([c1c16ff](https://github.com/PurrNet/PurrNet/commit/c1c16fff1692dd56c0db009e468ac87970d11adf))

## [1.12.3-beta.3](https://github.com/PurrNet/PurrNet/compare/v1.12.3-beta.2...v1.12.3-beta.3) (2025-06-27)


### Bug Fixes

* whitelist dirty wasn't being executed ([7bb9351](https://github.com/PurrNet/PurrNet/commit/7bb93511c5afe551dfa5c73efa29aaad5161120c))

## [1.12.3-beta.2](https://github.com/PurrNet/PurrNet/compare/v1.12.3-beta.1...v1.12.3-beta.2) (2025-06-27)


### Bug Fixes

* still prefer to call empty constructor instead of always initializing it to 0 ([5c667ac](https://github.com/PurrNet/PurrNet/commit/5c667ace880ba56b0a7b2aeb01066fcb60330fe0))

## [1.12.3-beta.1](https://github.com/PurrNet/PurrNet/compare/v1.12.2...v1.12.3-beta.1) (2025-06-27)


### Bug Fixes

* records ([983728a](https://github.com/PurrNet/PurrNet/commit/983728a6befc13b375ae4b8e5bbde8ed63c2cdbe))

## [1.12.2](https://github.com/PurrNet/PurrNet/compare/v1.12.1...v1.12.2) (2025-06-26)


### Bug Fixes

* boost IL processing performance ([7d32309](https://github.com/PurrNet/PurrNet/commit/7d32309df8c4f0cbf2951d806528df25ddde2c8e))
* composite transport ([4c84b41](https://github.com/PurrNet/PurrNet/commit/4c84b41640a817a6e01f4ba72d8d18af252dec03))
* do ownership stuff on early observer added ([e5724c6](https://github.com/PurrNet/PurrNet/commit/e5724c6d37a8c5dab40f6fe5cd21c7570deaa8c1))
* proper comparer ([a30043c](https://github.com/PurrNet/PurrNet/commit/a30043c802391a2b98ad65502e93d1012f7edef8))

## [1.12.2-beta.4](https://github.com/PurrNet/PurrNet/compare/v1.12.2-beta.3...v1.12.2-beta.4) (2025-06-26)


### Bug Fixes

* composite transport ([4c84b41](https://github.com/PurrNet/PurrNet/commit/4c84b41640a817a6e01f4ba72d8d18af252dec03))

## [1.12.2-beta.3](https://github.com/PurrNet/PurrNet/compare/v1.12.2-beta.2...v1.12.2-beta.3) (2025-06-26)


### Bug Fixes

* proper comparer ([a30043c](https://github.com/PurrNet/PurrNet/commit/a30043c802391a2b98ad65502e93d1012f7edef8))

## [1.12.2-beta.2](https://github.com/PurrNet/PurrNet/compare/v1.12.2-beta.1...v1.12.2-beta.2) (2025-06-26)


### Bug Fixes

* boost IL processing performance ([7d32309](https://github.com/PurrNet/PurrNet/commit/7d32309df8c4f0cbf2951d806528df25ddde2c8e))

## [1.12.2-beta.1](https://github.com/PurrNet/PurrNet/compare/v1.12.1...v1.12.2-beta.1) (2025-06-26)


### Bug Fixes

* do ownership stuff on early observer added ([e5724c6](https://github.com/PurrNet/PurrNet/commit/e5724c6d37a8c5dab40f6fe5cd21c7570deaa8c1))

## [1.12.1](https://github.com/PurrNet/PurrNet/compare/v1.12.0...v1.12.1) (2025-06-25)


### Bug Fixes

* check if networkAssets isnt null ([1038e1a](https://github.com/PurrNet/PurrNet/commit/1038e1a1e90af75a4b6de4bdac8888fdda06f2f5))

## [1.12.1-beta.1](https://github.com/PurrNet/PurrNet/compare/v1.12.0...v1.12.1-beta.1) (2025-06-25)


### Bug Fixes

* check if networkAssets isnt null ([1038e1a](https://github.com/PurrNet/PurrNet/commit/1038e1a1e90af75a4b6de4bdac8888fdda06f2f5))

# [1.12.0](https://github.com/PurrNet/PurrNet/compare/v1.11.1...v1.12.0) (2025-06-25)


### Bug Fixes

* `GetSpawnedParent` can throw an error ([513ce28](https://github.com/PurrNet/PurrNet/commit/513ce2845c0bcc6ec06ed9ed9574219e32d58d41))
* actually call Optimize on network animator batch ([a53f8e3](https://github.com/PurrNet/PurrNet/commit/a53f8e327ea65cceb56ff09e0a884cda6c152a2c))
* add `ServerOnlyAttribute` ([747451a](https://github.com/PurrNet/PurrNet/commit/747451a54e121107f3a87f2d22238a9eca255e87))
* add a `AlwaysIncludeDontDestroyOnLoadScene` in the network rules ([9404b77](https://github.com/PurrNet/PurrNet/commit/9404b778a7fbbe5a18201886da52f4c7f3524be6))
* add onPreProcessRpc and onPostProcessRpc to the RPCModule ([c588685](https://github.com/PurrNet/PurrNet/commit/c5886856b03a774452fd0618e480baefa2bb0655))
* added a changelog ([13af73d](https://github.com/PurrNet/PurrNet/commit/13af73dceddb751b26a8d25f37d485fe79706a25))
* Allow to save bandwidth to file and then load it in the editor ([2117e33](https://github.com/PurrNet/PurrNet/commit/2117e3355b268ef455f5c56cc13d05612f33098c))
* always gen the rpc signature ([1afa09c](https://github.com/PurrNet/PurrNet/commit/1afa09c6e0c45971251da0f9a395bd281ed0074c))
* batch acks for delta module ([cc4c89d](https://github.com/PurrNet/PurrNet/commit/cc4c89dbe46d2c29e717a52d4968a0226dd5cfa5))
* better error for when sync modules miss permissions ([e28df7b](https://github.com/PurrNet/PurrNet/commit/e28df7b9587fcdf47a7ae799f0c4bb9bcda16920))
* better static generic type discovery ([da5f6e9](https://github.com/PurrNet/PurrNet/commit/da5f6e954ed4727c6f09034ab8291c0036f95a93))
* better visibility API ([3af2c32](https://github.com/PurrNet/PurrNet/commit/3af2c32f62426564feb14db552412c66ed8bfd84))
* BitPacker being in Write mode when received for Reading ([7ebb8aa](https://github.com/PurrNet/PurrNet/commit/7ebb8aa45a3fb6f3283f977da42ca44100f84c9f))
* change name of package for openupm ([b759197](https://github.com/PurrNet/PurrNet/commit/b759197c0a11986a029e7caf333d3fe44655e5da))
* copy managed types when calling RPCs locally ([28b7091](https://github.com/PurrNet/PurrNet/commit/28b70917a70429f84332b1acefcc82fedf6bf272))
* DontPackAttribute only works for field ([5846ecd](https://github.com/PurrNet/PurrNet/commit/5846ecd9a5c4f2d9a07e41361f64e67ac8ddb0ec))
* ensure that it at least replaces with empty method for `ServerOnly` ([9750c5d](https://github.com/PurrNet/PurrNet/commit/9750c5d620e05c10421c0f0578451285d58358eb))
* enum delta packers weren't implemented ([13ed11f](https://github.com/PurrNet/PurrNet/commit/13ed11f922651136ee52b3e7ab09a91c7ca52902))
* Expanded the rtt summary ([7668055](https://github.com/PurrNet/PurrNet/commit/766805521bacdba984a15deb9f8011aed71c78c5))
* if server, always use the ownerServer value ([9626f51](https://github.com/PurrNet/PurrNet/commit/9626f513957ec5db316e27807bc622786820879e))
* improved statistics manager ([8fed412](https://github.com/PurrNet/PurrNet/commit/8fed412172ffdb88d74d7b80c1d093052f10644c))
* include full type for generic too ([4990d69](https://github.com/PurrNet/PurrNet/commit/4990d6983b059c20252c9dafd80250c6b93824e0))
* introduced DontPack attribute ([2fea79e](https://github.com/PurrNet/PurrNet/commit/2fea79e8cc8e2598001e29ab73b51fe4feaf7eb9))
* LastNID patch, this needs to be reworked ([16dc6d3](https://github.com/PurrNet/PurrNet/commit/16dc6d30cec6c85eb8fad123be0a3bfee2299a5a))
* link the changlog ([9ef043a](https://github.com/PurrNet/PurrNet/commit/9ef043a70732867218d4aaf98f0d2e7c0c38fbf0))
* make core unity dependencies optional ([12b06e1](https://github.com/PurrNet/PurrNet/commit/12b06e191792bb7d1c7416621c2c500af044f935))
* metadata file for CHANGELOG.md ([dd139fc](https://github.com/PurrNet/PurrNet/commit/dd139fc066987c8942d8751d6f194a917fa9616c))
* missing using ([0f51df2](https://github.com/PurrNet/PurrNet/commit/0f51df2921e55dc28c483d4efe444267dc14fab5))
* Network assets pull multiple sub assets ([de49d8b](https://github.com/PurrNet/PurrNet/commit/de49d8b07fdabb9057336bfef4317c806e7d6357))
* Network Assets working with Sub-assets ([769ff32](https://github.com/PurrNet/PurrNet/commit/769ff32e111da0315d6c077c0e1c8e41902a8900))
* network reflection and network assets ([1adea71](https://github.com/PurrNet/PurrNet/commit/1adea71cf4a1517122a5130429500a4a99ece8fa))
* only keep latest `SetX` for animation ([badec0d](https://github.com/PurrNet/PurrNet/commit/badec0dd5b6f56b88085f4e1ea6195ff4a3d33cf))
* ownership events ([9a245f9](https://github.com/PurrNet/PurrNet/commit/9a245f9c7dd4a9a70da9daa2fd27c57db84b711f))
* properly populate RPCInfo for runlocally ([bd99145](https://github.com/PurrNet/PurrNet/commit/bd991450479f1b09bff4e2be463e9cfd8c9b567a))
* refactoring `AreEqual` helpers for the packer ([20b2c70](https://github.com/PurrNet/PurrNet/commit/20b2c70665be9960e6df05776ebe261e53a45c7b))
* remove UniTask as a dependency ([725cabf](https://github.com/PurrNet/PurrNet/commit/725cabfc54a037375e94fb16ccbcb2e1d94aead7))
* reverted bad changes ([94914f4](https://github.com/PurrNet/PurrNet/commit/94914f4b907105abf1f4646551d61210c706eff4))
* server rpc's on server should not use the network ([06b6d9d](https://github.com/PurrNet/PurrNet/commit/06b6d9d15a78c7b908367af60ffea1e1137b9115))
* set target frame rate to tick rate for server builds ([b1fc358](https://github.com/PurrNet/PurrNet/commit/b1fc35896b66e2ea69f13910962e1a82199787c7))
* start server/client, stop server/client always calls the network manager and does it through it instead of individually, otherwise things are unpredictable ([157d47c](https://github.com/PurrNet/PurrNet/commit/157d47cd8405893fd0180b9621f58fc3e6da788b))
* state machine editor issues in prefab runtime ([d0ad04a](https://github.com/PurrNet/PurrNet/commit/d0ad04a033fe5e0d860cdd11a6d1cd9be8a16c46))
* State machine exit on despawn ([9884c58](https://github.com/PurrNet/PurrNet/commit/9884c585b1aa8950b56fbc7db82d58d1039bc864))
* Statistics manager improvements ([f494ce9](https://github.com/PurrNet/PurrNet/commit/f494ce96b947ea8a69d049ed50adc39ab4432ac6))
* Statistics manager jitter ([0c5d611](https://github.com/PurrNet/PurrNet/commit/0c5d611b215a5d049c3494c58c189b3b5c4ff8b9))
* steam server not properly cleaning internal state ([af3a793](https://github.com/PurrNet/PurrNet/commit/af3a7932271bf7547e8d14bfc23a26e539aa3445))
* Sync dictionary sending for clients ([88ce60a](https://github.com/PurrNet/PurrNet/commit/88ce60a2f56e5d594a9f2c54b055eaef8790d4b9))
* Sync types for strict rules ([7722477](https://github.com/PurrNet/PurrNet/commit/7722477cba75fc22b49c6b23af70d4e4b5d57132))
* undo mess ([9f0f26c](https://github.com/PurrNet/PurrNet/commit/9f0f26c336b16ec78d6f340dd529286cf5c05fad))
* unityProxyType being null caused IL issues ([15a85cd](https://github.com/PurrNet/PurrNet/commit/15a85cd3b10ec0865965ad5fa190a68467879f3c))
* weird ownership order ([634ed88](https://github.com/PurrNet/PurrNet/commit/634ed88a8098049f9455cda503b0f5eb7cf7a96e))
* when sending a target rpc to local player just call it locally ([2982811](https://github.com/PurrNet/PurrNet/commit/2982811a01626b4f0cdf0da0378c5c25a26aa2ff))


### Features

* Network assets added ([16ebe3c](https://github.com/PurrNet/PurrNet/commit/16ebe3c4e91db8ab14f0d7c075294bae0354f33c))
* unity editor toolbar with purrnet state ([dbdb6cb](https://github.com/PurrNet/PurrNet/commit/dbdb6cb04ac88fb364826430c2a32273ad8e79b8))

# [1.12.0-beta.11](https://github.com/PurrNet/PurrNet/compare/v1.12.0-beta.10...v1.12.0-beta.11) (2025-06-25)


### Bug Fixes

* BitPacker being in Write mode when received for Reading ([7ebb8aa](https://github.com/PurrNet/PurrNet/commit/7ebb8aa45a3fb6f3283f977da42ca44100f84c9f))
* network reflection and network assets ([1adea71](https://github.com/PurrNet/PurrNet/commit/1adea71cf4a1517122a5130429500a4a99ece8fa))

# [1.12.0-beta.10](https://github.com/PurrNet/PurrNet/compare/v1.12.0-beta.9...v1.12.0-beta.10) (2025-06-24)


### Bug Fixes

* set target frame rate to tick rate for server builds ([b1fc358](https://github.com/PurrNet/PurrNet/commit/b1fc35896b66e2ea69f13910962e1a82199787c7))

# [1.12.0-beta.9](https://github.com/PurrNet/PurrNet/compare/v1.12.0-beta.8...v1.12.0-beta.9) (2025-06-24)


### Bug Fixes

* Network assets pull multiple sub assets ([de49d8b](https://github.com/PurrNet/PurrNet/commit/de49d8b07fdabb9057336bfef4317c806e7d6357))

# [1.12.0-beta.8](https://github.com/PurrNet/PurrNet/compare/v1.12.0-beta.7...v1.12.0-beta.8) (2025-06-24)


### Bug Fixes

* Network Assets working with Sub-assets ([769ff32](https://github.com/PurrNet/PurrNet/commit/769ff32e111da0315d6c077c0e1c8e41902a8900))

# [1.12.0-beta.7](https://github.com/PurrNet/PurrNet/compare/v1.12.0-beta.6...v1.12.0-beta.7) (2025-06-24)


### Bug Fixes

* ownership events ([9a245f9](https://github.com/PurrNet/PurrNet/commit/9a245f9c7dd4a9a70da9daa2fd27c57db84b711f))

# [1.12.0-beta.6](https://github.com/PurrNet/PurrNet/compare/v1.12.0-beta.5...v1.12.0-beta.6) (2025-06-23)


### Bug Fixes

* Statistics manager jitter ([0c5d611](https://github.com/PurrNet/PurrNet/commit/0c5d611b215a5d049c3494c58c189b3b5c4ff8b9))

# [1.12.0-beta.5](https://github.com/PurrNet/PurrNet/compare/v1.12.0-beta.4...v1.12.0-beta.5) (2025-06-22)


### Bug Fixes

* include full type for generic too ([4990d69](https://github.com/PurrNet/PurrNet/commit/4990d6983b059c20252c9dafd80250c6b93824e0))

# [1.12.0-beta.4](https://github.com/PurrNet/PurrNet/compare/v1.12.0-beta.3...v1.12.0-beta.4) (2025-06-22)


### Bug Fixes

* better static generic type discovery ([da5f6e9](https://github.com/PurrNet/PurrNet/commit/da5f6e954ed4727c6f09034ab8291c0036f95a93))

# [1.12.0-beta.3](https://github.com/PurrNet/PurrNet/compare/v1.12.0-beta.2...v1.12.0-beta.3) (2025-06-22)


### Bug Fixes

* Expanded the rtt summary ([7668055](https://github.com/PurrNet/PurrNet/commit/766805521bacdba984a15deb9f8011aed71c78c5))

# [1.12.0-beta.2](https://github.com/PurrNet/PurrNet/compare/v1.12.0-beta.1...v1.12.0-beta.2) (2025-06-22)


### Features

* unity editor toolbar with purrnet state ([dbdb6cb](https://github.com/PurrNet/PurrNet/commit/dbdb6cb04ac88fb364826430c2a32273ad8e79b8))

# [1.12.0-beta.1](https://github.com/PurrNet/PurrNet/compare/v1.11.2-beta.41...v1.12.0-beta.1) (2025-06-20)


### Features

* Network assets added ([16ebe3c](https://github.com/PurrNet/PurrNet/commit/16ebe3c4e91db8ab14f0d7c075294bae0354f33c))

## [1.11.2-beta.41](https://github.com/PurrNet/PurrNet/compare/v1.11.2-beta.40...v1.11.2-beta.41) (2025-06-20)


### Bug Fixes

* weird ownership order ([634ed88](https://github.com/PurrNet/PurrNet/commit/634ed88a8098049f9455cda503b0f5eb7cf7a96e))

## [1.11.2-beta.40](https://github.com/PurrNet/PurrNet/compare/v1.11.2-beta.39...v1.11.2-beta.40) (2025-06-20)


### Bug Fixes

* link the changlog ([9ef043a](https://github.com/PurrNet/PurrNet/commit/9ef043a70732867218d4aaf98f0d2e7c0c38fbf0))

## [1.11.2-beta.39](https://github.com/PurrNet/PurrNet/compare/v1.11.2-beta.38...v1.11.2-beta.39) (2025-06-20)


### Bug Fixes

* metadata file for CHANGELOG.md ([dd139fc](https://github.com/PurrNet/PurrNet/commit/dd139fc066987c8942d8751d6f194a917fa9616c))

## [1.11.2-beta.38](https://github.com/PurrNet/PurrNet/compare/v1.11.2-beta.37...v1.11.2-beta.38) (2025-06-20)


### Bug Fixes

* added a changelog ([13af73d](https://github.com/PurrNet/PurrNet/commit/13af73dceddb751b26a8d25f37d485fe79706a25))

# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

<!-- This section will be automatically populated by semantic-release -->

<!--
## [1.0.0] - YYYY-MM-DD
### Added
- New features

### Changed
- Changes in existing functionality

### Deprecated
- Soon-to-be removed features

### Removed
- Removed features

### Fixed
- Bug fixes

### Security
- Vulnerability fixes
-->
