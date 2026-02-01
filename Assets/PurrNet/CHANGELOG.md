# 1.0.0-beta.1 (2026-02-01)


### Bug Fixes

* @RoxDevvv revamped the `NetworkOwnershipToggle` inspector ([8fc338e](https://github.com/GZMOS/PurrNet/commit/8fc338e5ccfea66ce628ef8c9e89fb73529b4f11))
* `_trs` being null on OnEarlySpawn for the `NetworkTransform` ([7da4ded](https://github.com/GZMOS/PurrNet/commit/7da4ded97222cc88deeea244c0b5b91d4bbaab3c))
* `GetSpawnedParent` can throw an error ([513ce28](https://github.com/GZMOS/PurrNet/commit/513ce2845c0bcc6ec06ed9ed9574219e32d58d41))
* `isIk` wasn't checking enough cases thx @OverGast ([4dd1c01](https://github.com/GZMOS/PurrNet/commit/4dd1c0133428365101c3bb28f087c9345ae0cc1e))
* +disposable array ([4c92504](https://github.com/GZMOS/PurrNet/commit/4c925042940abf14b839d1b5b6a7063a56be34b2))
* 2022 compiler error ([888b003](https://github.com/GZMOS/PurrNet/commit/888b003eb6ebe5c00b9e5b624d64d2205a6c37c4))
* actions wasnt working with the release branch ([ab1b203](https://github.com/GZMOS/PurrNet/commit/ab1b20348d4a5038e7ae1680b33c10e822e0cf58))
* actual il fixes ([6a8176e](https://github.com/GZMOS/PurrNet/commit/6a8176e99ebb8ae73c7a6cd99d98001cadfe2248))
* ActualGetRelayServersAsync sometimes would come with an empty string and throw and exception ([c186213](https://github.com/GZMOS/PurrNet/commit/c1862139fb17591e678acfc40a9ded9886b5ee83))
* actually call Optimize on network animator batch ([a53f8e3](https://github.com/GZMOS/PurrNet/commit/a53f8e327ea65cceb56ff09e0a884cda6c152a2c))
* actually register disposableArray ([c0f7b0f](https://github.com/GZMOS/PurrNet/commit/c0f7b0f85213304459111acbaa607dd834dbf7e1))
* add `HierarchyV2.onPreSpawn` static event ([0c02749](https://github.com/GZMOS/PurrNet/commit/0c0274922d2cfb1db576c4bb4fbfa4d1e73f50f6))
* add `ServerOnlyAttribute` ([747451a](https://github.com/GZMOS/PurrNet/commit/747451a54e121107f3a87f2d22238a9eca255e87))
* add `ShouldCleanupScenesOnDisconnect` rule ([dd40514](https://github.com/GZMOS/PurrNet/commit/dd4051463941456f3e07000a8496e4cd53166ae6))
* add `ShouldCleanupSpawnedObjectsOnDisconnect` to network rules ([7d05d50](https://github.com/GZMOS/PurrNet/commit/7d05d50358fa94851b32c0de312959931ac4b0f5))
* add a `AlwaysIncludeDontDestroyOnLoadScene` in the network rules ([9404b77](https://github.com/GZMOS/PurrNet/commit/9404b778a7fbbe5a18201886da52f4c7f3524be6))
* add aggressive inlining to Duplicate method for JIT performance improvement ([f46516e](https://github.com/GZMOS/PurrNet/commit/f46516e8aafcd64759874fd3546148bb5cc06ce4))
* add asServer for collider registration (rollback) ([92390cf](https://github.com/GZMOS/PurrNet/commit/92390cfda5053c55d005d28bd6c901ad6ee9af7b))
* add debugging scripting symbol for delta compression that packs extra data to let you know of issues ([2c96204](https://github.com/GZMOS/PurrNet/commit/2c9620425c2ae43e545bbc04ed94205f02e75d6e))
* add deterministic hash to the packer ([ceaaf4d](https://github.com/GZMOS/PurrNet/commit/ceaaf4d08db74443a67bdadf533bcd5d955d2c90))
* add DisposableDictionary along side it's pool ([baa2c99](https://github.com/GZMOS/PurrNet/commit/baa2c9962539222ae62a203499712db0285321ce))
* add LocalMode attribute! ([23703e0](https://github.com/GZMOS/PurrNet/commit/23703e0e6201c5d238c231a920ff134c1bb12e55))
* add missing GetFloat(int nameHash) ([db2d7ce](https://github.com/GZMOS/PurrNet/commit/db2d7ce1603bc86ade3ac473290cce25d1c507bb))
* add network manager to rpc info ([960f2f8](https://github.com/GZMOS/PurrNet/commit/960f2f822e58c6b11e59580a13432d18a8200027))
* add onPreProcessRpc and onPostProcessRpc to the RPCModule ([c588685](https://github.com/GZMOS/PurrNet/commit/c5886856b03a774452fd0618e480baefa2bb0655))
* add PurrSceneInfo to all damn root objects ([35765ab](https://github.com/GZMOS/PurrNet/commit/35765ab171faa0d10dff6075dca7f9550e6c30b0))
* add StartHost variant with summary ([01619d1](https://github.com/GZMOS/PurrNet/commit/01619d1ba860c7dfc9028e13f89bbfaaad837a75))
* add UTF8 encoding helper ([68409ce](https://github.com/GZMOS/PurrNet/commit/68409ce42576c10fe36692e15172375b33a6ff75))
* add writers for GameObject and Transform ([a569809](https://github.com/GZMOS/PurrNet/commit/a5698094f6be3081188d035cf43b33e4669cf4e6))
* added a changelog ([13af73d](https://github.com/GZMOS/PurrNet/commit/13af73dceddb751b26a8d25f37d485fe79706a25))
* Added ability to add and remove state nodes dynamically ([35dcc5f](https://github.com/GZMOS/PurrNet/commit/35dcc5f3974c5a30cc3f8b3bda53c404cb82bc67))
* Added action subscription to syncevent ([e30ed2a](https://github.com/GZMOS/PurrNet/commit/e30ed2a40f406fe833f5453cbdba4bfdf4fa32ec))
* Added connection UI example ([36be104](https://github.com/GZMOS/PurrNet/commit/36be104386c6237c5c6222a33b362906f30b4f32))
* Added create overloads for disposable types ([6650be1](https://github.com/GZMOS/PurrNet/commit/6650be1301666f0c87fa68d4a0bc6c7f0d9fcb4e))
* Added custom unity web request extensions for build support in earlier versions ([ac64eb4](https://github.com/GZMOS/PurrNet/commit/ac64eb4feb001b29b776f21bc5b623c1b3dd2d49))
* Added Disposable HashSet creation ([9f1b2e3](https://github.com/GZMOS/PurrNet/commit/9f1b2e3dfdf30cc56960b631dd56147ae7563671))
* Added disposable list static creation ([101cf00](https://github.com/GZMOS/PurrNet/commit/101cf009c2bf5157a4e6cdeba973d81c0e4b54f7))
* Added dynamic force correction to network RB ([c6a27cf](https://github.com/GZMOS/PurrNet/commit/c6a27cfbac65ec379c33c88bc8d939091a2b4ed7))
* Added getter for previous state node ([598d575](https://github.com/GZMOS/PurrNet/commit/598d57520b9e6a291e5c779a3d2ec21eb8767268))
* Added git checking to addon library ([336e590](https://github.com/GZMOS/PurrNet/commit/336e59027c71b3ed96a6dc6d8f4f5a699a6af213))
* Added list getter on SyncList ([75f487f](https://github.com/GZMOS/PurrNet/commit/75f487ff3a91c950291a1e5be14a0e896ef06f20))
* Added previous valid to state machine ([3b2f261](https://github.com/GZMOS/PurrNet/commit/3b2f261e9fe2a488e96f73927be6ca57f1bc45f1))
* Added proper asset post processing to network assets ([c10f7fe](https://github.com/GZMOS/PurrNet/commit/c10f7feade5ad191f5fea8b1d1a3f2d32a3f64ef))
* Added PurrChat link to toolbar ([72d3d58](https://github.com/GZMOS/PurrNet/commit/72d3d58cf6be44a925837f736c49b7a1af92e93b))
* added purrnet settings to strip server code (experimental) ([b338ff1](https://github.com/GZMOS/PurrNet/commit/b338ff11332e49668ef1f809f0bd3ca8127edf72))
* Added state machine current state helpers ([6ebfff9](https://github.com/GZMOS/PurrNet/commit/6ebfff9a934aadbf3e0c3010fff2632751e3538d))
* Added statistics manager display target options ([75d095f](https://github.com/GZMOS/PurrNet/commit/75d095ff1881ed47d836c9af63baf21c90bc09e9))
* Added summaries to SyncInput ([bc21113](https://github.com/GZMOS/PurrNet/commit/bc21113ee69cc989ef2b0a115baecc245a210a2f))
* Added summary for new sync timer advancing ([0b5be52](https://github.com/GZMOS/PurrNet/commit/0b5be5285c2458e1351da220a6c1088699514d2f))
* Added sync event without data ([4674dfe](https://github.com/GZMOS/PurrNet/commit/4674dfea96fef1cfbf4b9e6618c76564415af52e))
* Added sync list initialization w. data ([28a588a](https://github.com/GZMOS/PurrNet/commit/28a588aff12f17281328d4000e4b184e26fa29c4))
* Added SyncInput network module ([b15f979](https://github.com/GZMOS/PurrNet/commit/b15f979493dc048be04784c5482241d7be94c977))
* Additional safety added to packer of gameobject and transform ([20f3623](https://github.com/GZMOS/PurrNet/commit/20f36236b4d6929a8bf1956ae52175ce09ad7824))
* Addon library fixed for manifest handling ([7b13f01](https://github.com/GZMOS/PurrNet/commit/7b13f013218777dc32b4536539915a21411e8e2c))
* Addon library image request handling improved ([49eccf7](https://github.com/GZMOS/PurrNet/commit/49eccf794307569694ad47d794a70ccca02cd322))
* allow DontPack to be at the type level ([354f271](https://github.com/GZMOS/PurrNet/commit/354f27178bc420b279c50c79a01e7c02fb09e2b5))
* allow for manual despawning too ([0a01be8](https://github.com/GZMOS/PurrNet/commit/0a01be8322a43085da4a0e8b9a9f22de16033ee5))
* allow for null values when reading classes with inheritance ([86db192](https://github.com/GZMOS/PurrNet/commit/86db1929d3d81367e362effb6537fa28abc511ac))
* allow for value modifiers for the delta module ([c0ddf66](https://github.com/GZMOS/PurrNet/commit/c0ddf665067973d5d28c2c63ad01a4a8c941ce1f))
* allow network animator to reconcile time ([caa62bc](https://github.com/GZMOS/PurrNet/commit/caa62bc81078a38cf5381dad5f888e6186ee3089))
* allow passing rpcs as parameters, aka Action<> etc ([fb87fef](https://github.com/GZMOS/PurrNet/commit/fb87fefe8c86c078ea27570ef97d928592dcdd3a))
* allow player spawner to ignore rules ([77a0f41](https://github.com/GZMOS/PurrNet/commit/77a0f4191e29cfad542b2baaefeb071cec62668b))
* allow PurrTransport.cs to kick connections ([f1fcc4a](https://github.com/GZMOS/PurrNet/commit/f1fcc4a72c34549dcb86a78f0567124563c4a433))
* allow reliable tick events ([18858ae](https://github.com/GZMOS/PurrNet/commit/18858ae8dbda628034540cc0ff0fe838a32c0af8))
* allow to dynamically register colliders for rollback history ([2d2762b](https://github.com/GZMOS/PurrNet/commit/2d2762b493afcc604c9e7e3e7fa3a24c50c42125))
* allow to filter purrnet's scene object discovery ([522ef9d](https://github.com/GZMOS/PurrNet/commit/522ef9d042983d83d886c63d05342a7a704d0f50))
* allow to get latest NT state without relying on execution order and weirdness with parenting ([ba502e8](https://github.com/GZMOS/PurrNet/commit/ba502e832c94e442be4b47231869c22c1b117547))
* allow to manually clear all subscriptions from network manaher using `ResetInternalState` ([f24f0a8](https://github.com/GZMOS/PurrNet/commit/f24f0a897d64b19a7d7108f6634b87ed2d844e3e))
* allow to not sync specific hashes ([6b65429](https://github.com/GZMOS/PurrNet/commit/6b654299060b9e8f1281033b57024df2f82d26e3))
* allow to override `propagateToChildren` when giving/removing ownership ([8fb5cf7](https://github.com/GZMOS/PurrNet/commit/8fb5cf73b2571e46e022bfd046f72a711f50bfb2))
* allow to override how things are duplicated ([c99ba8f](https://github.com/GZMOS/PurrNet/commit/c99ba8f122f6b10bf1bf7f335d1b1df623ec7781))
* allow to prioritize non generated packers more explicitly ([0081fbc](https://github.com/GZMOS/PurrNet/commit/0081fbc4d9a25432ce31ac9807dea7ea0a151ab4))
* Allow to save bandwidth to file and then load it in the editor ([2117e33](https://github.com/GZMOS/PurrNet/commit/2117e3355b268ef455f5c56cc13d05612f33098c))
* allow to set extra bones for the NetworkBones component ([7e7872d](https://github.com/GZMOS/PurrNet/commit/7e7872d60c733c7d135a43a95e7c3d6f0a4b70d3))
* allow to skip scene auto spawning ([204987c](https://github.com/GZMOS/PurrNet/commit/204987c50ded5d0b76dc5a83a6c7a8e264a95c80))
* allow to teleport network transform (aka clear interpolation) ([8dd6f33](https://github.com/GZMOS/PurrNet/commit/8dd6f3359f860b6090c93df265950d2efc20fa67))
* allow UDP transport to poll events on update ([8c43b15](https://github.com/GZMOS/PurrNet/commit/8c43b15df92be19f795a97fb90357a29ead58b4f))
* Allowing NT to easily force sync ([8eb4178](https://github.com/GZMOS/PurrNet/commit/8eb41781173a4e11b150a55a71fd942ae3a10561))
* also cleanup on destroy ([2d47451](https://github.com/GZMOS/PurrNet/commit/2d47451772812bf5caf09c6c7af4e69d602c3485))
* also render purrnet toolbar on clones ([c3dbb1c](https://github.com/GZMOS/PurrNet/commit/c3dbb1cd127403cba11f5a9c415a82e6679b38e4))
* always gen the rpc signature ([1afa09c](https://github.com/GZMOS/PurrNet/commit/1afa09c6e0c45971251da0f9a395bd281ed0074c))
* always lerp, don't cache the value ([8f2b116](https://github.com/GZMOS/PurrNet/commit/8f2b116bfb9e092d51e592db5b4958201d2c0d29))
* always prepare the hash for `System.Object` ([5315b82](https://github.com/GZMOS/PurrNet/commit/5315b823ccb6edc1119b640c669b41538e711c8e))
* angle precision ([6e58aa3](https://github.com/GZMOS/PurrNet/commit/6e58aa3653f102c8b4ca70f4bca7b6de3765aa3a))
* array comparison ([a278892](https://github.com/GZMOS/PurrNet/commit/a278892d44e3ca79d30a9d5becdc5469f07cff52))
* assembly.GetTypes exception ruining my dreams ([2613654](https://github.com/GZMOS/PurrNet/commit/261365437184ad226c447766607e64b02d4abdee))
* async rpcs from server to server failed to complete ([0f320d3](https://github.com/GZMOS/PurrNet/commit/0f320d3ad805481f7ef2e0a1a46d578c52a85222))
* async Task compiler error for IL2CPP ([cc40fc0](https://github.com/GZMOS/PurrNet/commit/cc40fc05db6b3c4ffc98ee4878bd634ad385c5e7))
* attempt at fixing steam issue ([6c96b84](https://github.com/GZMOS/PurrNet/commit/6c96b8432ce3b6a94f29da7d01f06110bea646b4))
* attempt to circumvent caching ([de0c54b](https://github.com/GZMOS/PurrNet/commit/de0c54b1f1c33ab3c7fac9d607b2d97bf83699d0))
* attempting to make networktransform more performant ([6a065f2](https://github.com/GZMOS/PurrNet/commit/6a065f210f1d2a6ad238c6a057c512d53fa93b66))
* authenticator documentation ([3e2c389](https://github.com/GZMOS/PurrNet/commit/3e2c389ecfac05025eaedfdc070802ca5aa42087))
* auto detect any network transforms and apply any interpolation offsets ([a1a2065](https://github.com/GZMOS/PurrNet/commit/a1a20656a91d1b7f25f2c8bbb8dfdf2b2a669629))
* auto spawning and secene prefabs ([d93fbf8](https://github.com/GZMOS/PurrNet/commit/d93fbf8ae283d15e364a4f69bfe6b94a90be084e))
* automatic sync parameters cache was invalid ([7330aed](https://github.com/GZMOS/PurrNet/commit/7330aedf6ef0e365e032697644a3f4ac12744d12))
* avoid adding null keys to dictionaries and hashsets ([2d934d5](https://github.com/GZMOS/PurrNet/commit/2d934d52e0ff6c4907d273c29caf14d95cbb2a8c))
* avoid reading data if bones aren't ready ([fca1a62](https://github.com/GZMOS/PurrNet/commit/fca1a62a2bba25bf840853afdf3bc7915e5d569d))
* Awaitable error on older versions ([7cd6ad9](https://github.com/GZMOS/PurrNet/commit/7cd6ad923ecfd4af2658728b2d0b337d8902b380))
* bad import of typeof(float) ([25327b3](https://github.com/GZMOS/PurrNet/commit/25327b37bd9f1128de50b3e5dda1c0e96ea55d7d))
* bad math ([592fb0e](https://github.com/GZMOS/PurrNet/commit/592fb0e02eef3c91e2d7626410467375b5fd74aa))
* bad symbol name ([0d94952](https://github.com/GZMOS/PurrNet/commit/0d949526be21c408ab0a979965fffde58b2a2ac2))
* base writing replace old pointer ([23573a4](https://github.com/GZMOS/PurrNet/commit/23573a43b0e024f68c6694d323b33c7f07694bdf))
* batch acks for delta module ([cc4c89d](https://github.com/GZMOS/PurrNet/commit/cc4c89dbe46d2c29e717a52d4968a0226dd5cfa5))
* batch despawn packets with spawn packets ([b396f3f](https://github.com/GZMOS/PurrNet/commit/b396f3f438eba0b334ec022c5126b4dc3b360430))
* batch initial spawn packet to ease the reliable channel ([5fdfdf6](https://github.com/GZMOS/PurrNet/commit/5fdfdf6d9719cefb0ad46ef32e89739f3d5f1d36))
* better `Transform` and `GameObject` packers ([6f820e6](https://github.com/GZMOS/PurrNet/commit/6f820e679533db3462168978c5b835577dcd6303))
* Better button placement ([26d2e12](https://github.com/GZMOS/PurrNet/commit/26d2e12f883553d39ad82c7721bc4b6cf6541af1))
* better caching for NetworkIdentity reflection stuff ([dfbaa43](https://github.com/GZMOS/PurrNet/commit/dfbaa433e87d3db09d9d49d30df1071b96bc7899))
* better cancellation for purrtransport ([e7bbc5f](https://github.com/GZMOS/PurrNet/commit/e7bbc5f8b105edc0e81564900922f21858ebc6f2))
* better error for unitask ([60e6ea2](https://github.com/GZMOS/PurrNet/commit/60e6ea222bc636c3f155d6c9329d0c62ba4cd069))
* better error for when sync modules miss permissions ([e28df7b](https://github.com/GZMOS/PurrNet/commit/e28df7b9587fcdf47a7ae799f0c4bb9bcda16920))
* better error messages when spawning fails ([0b6b99f](https://github.com/GZMOS/PurrNet/commit/0b6b99f09078a14dda883f86905c25f237e28dda))
* better fallback serializers for delta compression ([2276832](https://github.com/GZMOS/PurrNet/commit/2276832f3f2ade89177e0550663aa4964361cd67))
* better fix for subscribing and asServer issue ([28f7607](https://github.com/GZMOS/PurrNet/commit/28f76077a506d116c7f013851ce6791c9644a4aa))
* better interface checking ([45cce33](https://github.com/GZMOS/PurrNet/commit/45cce33dd652e46113c1670911767211b720ea0c))
* better internal packer resizing calc ([ea6f39d](https://github.com/GZMOS/PurrNet/commit/ea6f39df6c0f66e242e183c083de1c6788f586db))
* better static generic type discovery ([da5f6e9](https://github.com/GZMOS/PurrNet/commit/da5f6e954ed4727c6f09034ab8291c0036f95a93))
* better visibility API ([3af2c32](https://github.com/GZMOS/PurrNet/commit/3af2c32f62426564feb14db552412c66ed8bfd84))
* Big cleanup of SyncEvent ([633a0ce](https://github.com/GZMOS/PurrNet/commit/633a0cebb936f4398d1d7b37f5802103dae9388b))
* big data bugs and sync texture! ([ca70b79](https://github.com/GZMOS/PurrNet/commit/ca70b79dc55cb42937dcdc2f041c3c9a1ec0e5c2))
* bit packer tests ([689c812](https://github.com/GZMOS/PurrNet/commit/689c812e443e0a916088c7e98dba554c03c95512))
* bit packet, include base classes ([47a3cd6](https://github.com/GZMOS/PurrNet/commit/47a3cd606d559141ebe8ea294c07583420dc4f72))
* BitPacker being in Write mode when received for Reading ([7ebb8aa](https://github.com/GZMOS/PurrNet/commit/7ebb8aa45a3fb6f3283f977da42ca44100f84c9f))
* Bitpacker updated for improved class handling ([9c42b92](https://github.com/GZMOS/PurrNet/commit/9c42b926e5404b7b5ed453fe31052a4467923549))
* boost IL processing performance ([7d32309](https://github.com/GZMOS/PurrNet/commit/7d32309df8c4f0cbf2951d806528df25ddde2c8e))
* BREAKING CHANGE fixed type in `AuthenticationBehaviour<T>`, renamed `GetClientPlayload` to `GetClientPayload` ([b03e333](https://github.com/GZMOS/PurrNet/commit/b03e333c40c3e637b67806041136c29df4ff3276))
* bring default ownership back ([ee39be5](https://github.com/GZMOS/PurrNet/commit/ee39be596dfd903832369fe3a9d8e494c581a6af))
* buffer settings for bones ([f4af0eb](https://github.com/GZMOS/PurrNet/commit/f4af0ebbace94885acd8c51e5b9c20ad32d1ce6b))
* buffered RPCs failing ([619d0d8](https://github.com/GZMOS/PurrNet/commit/619d0d8557e67b6fe5e96c9ecf6bf8c65f80e85f))
* buffered RPCs sent too soon ([4e8c36d](https://github.com/GZMOS/PurrNet/commit/4e8c36d9ab5cf806d80ba85fae47774703ea1953))
* buffering for forwarded RPCs ([f539e0e](https://github.com/GZMOS/PurrNet/commit/f539e0e91f55f64b9b311a6937f25e09bc6fa897))
* build version info missing ([7befbe1](https://github.com/GZMOS/PurrNet/commit/7befbe1c4579a7ca3799d3d931a09860944af004))
* building errors ([d0ce345](https://github.com/GZMOS/PurrNet/commit/d0ce34535f668d9bad6759385d3bf3c455d12c83))
* bump version ([2de95c9](https://github.com/GZMOS/PurrNet/commit/2de95c9c8a3694410e1296e317217cae98806d98))
* bump version ([d005f0e](https://github.com/GZMOS/PurrNet/commit/d005f0eab61f768c1fa18e856055ad1799123d3c))
* bump version ([4d8a805](https://github.com/GZMOS/PurrNet/commit/4d8a805135d239d7806baa286b6469b55fd3958e))
* ByteData serializing with size for RPC response ([af4da7f](https://github.com/GZMOS/PurrNet/commit/af4da7fb485f6d641e5d080a1691877228b2d3f9))
* cache `CallInitMethods` results ([70e4d44](https://github.com/GZMOS/PurrNet/commit/70e4d449efdcd7861a0f48a5574cf11d6c5fd8ee))
* cache original scene build index ([93c71de](https://github.com/GZMOS/PurrNet/commit/93c71dec730fdfe30e42f75ff5c7a68321d7d1e8))
* call UnAuthenticateClient when player leaves ([2fe172f](https://github.com/GZMOS/PurrNet/commit/2fe172f21aa0deee5f609d47ede5f83d2d1a6100))
* change default to not be 'dont destroy on load' ([adf889d](https://github.com/GZMOS/PurrNet/commit/adf889d567c829f10ce3f696b457de781b8e88b8))
* change name of package for openupm ([b759197](https://github.com/GZMOS/PurrNet/commit/b759197c0a11986a029e7caf333d3fe44655e5da))
* changed scene indexing approach ([6d8bf03](https://github.com/GZMOS/PurrNet/commit/6d8bf03980ec09200497de4219456d2496d7551f))
* Character controller patch added to Network Transform ([67546f4](https://github.com/GZMOS/PurrNet/commit/67546f4da9670784faf95d79eb935f8c9ec0b2a3))
* check if networkAssets isnt null ([1038e1a](https://github.com/GZMOS/PurrNet/commit/1038e1a1e90af75a4b6de4bdac8888fdda06f2f5))
* cleanup can run into destroyed identities ([539dd76](https://github.com/GZMOS/PurrNet/commit/539dd768b28e26c6db09ef676dd40e543ea66e62))
* cleanup issues ([36abd59](https://github.com/GZMOS/PurrNet/commit/36abd590f60caa6e79d7788e4871805b1014ab0e))
* cleanup modules ([729fc3a](https://github.com/GZMOS/PurrNet/commit/729fc3a8330aef563fe1c4d2d15f583999506403))
* cleanup server/client modules, this caused some issues when next playthrough starting mode was changed ([65b2d0c](https://github.com/GZMOS/PurrNet/commit/65b2d0c6a330514edfb40d28c37cb75716b9a00d))
* cleanup spawnpoints ([333e26e](https://github.com/GZMOS/PurrNet/commit/333e26e4a168682d47546e2e49f5fd976a6760e3))
* clear connections when SteamTransport starts ([c77f166](https://github.com/GZMOS/PurrNet/commit/c77f16671bc0f488e10df8fca4db1bb96d438a6f))
* clear partial data if owner disconnected mid download ([0bb0cd9](https://github.com/GZMOS/PurrNet/commit/0bb0cd973e0badcaa4ccea88320b9bc44f477408))
* clear static stuff for PlayerIdentity.cs ([da22af9](https://github.com/GZMOS/PurrNet/commit/da22af9c0d1b433e700130484622259dd763ca33))
* Clearing instance handler properly ([c490a04](https://github.com/GZMOS/PurrNet/commit/c490a04490cbf4f7c57fa4323196e50d6f317c3b))
* client default to owner fixes for host mode ([3e98bcf](https://github.com/GZMOS/PurrNet/commit/3e98bcff588178d196440be8ea32d5102915a808))
* client to client RPCs ([eb42459](https://github.com/GZMOS/PurrNet/commit/eb4245996453f51445ea88a946dbf79c63406ea7))
* collider rollback for 2D physics ([b7f79a4](https://github.com/GZMOS/PurrNet/commit/b7f79a4fe592ddca75e586ad9c58ed0a7a6f9b27))
* Collider3DExtensions for other casting methods ([dfeac1f](https://github.com/GZMOS/PurrNet/commit/dfeac1f088ce9fb1f79d18d58fb43472fa2801d4))
* commit package.json ([863d7fc](https://github.com/GZMOS/PurrNet/commit/863d7fcd47f4288c6453605308182280bf2c2a04))
* Compare synclist delta when receiving full state ([24aca2f](https://github.com/GZMOS/PurrNet/commit/24aca2f5efa6f3c4595e9baed3effe3561a5bc6f))
* compiler error due to bad #if ([41cc6c6](https://github.com/GZMOS/PurrNet/commit/41cc6c62928d1f9dd037751e9f44b729b82399c8))
* compiler error for network module due to rework ([2a5a3e5](https://github.com/GZMOS/PurrNet/commit/2a5a3e5701011880acb580f392b2fdbbba673a88))
* compiler warnings in tick manager ([7fa1d3b](https://github.com/GZMOS/PurrNet/commit/7fa1d3b3fa8cbb17d98a76a2a8639f633ff3b697))
* composite transport ([4c84b41](https://github.com/GZMOS/PurrNet/commit/4c84b41640a817a6e01f4ba72d8d18af252dec03))
* composite transport multiplying events on reconnect ([285eac3](https://github.com/GZMOS/PurrNet/commit/285eac383ead386f39db5086a7d88838d04431d1))
* composite transport server shutdown out of range exception ([17ec4db](https://github.com/GZMOS/PurrNet/commit/17ec4db2891d9d5ca770e5441e53edad5f8117e7))
* compressed float equality failed, stick to storing raw value instead of float value ([2d04a76](https://github.com/GZMOS/PurrNet/commit/2d04a76d83fedd6a2c9f93e10d6c23338d92cb12))
* compression was broken due to silly mistake ([781ba5a](https://github.com/GZMOS/PurrNet/commit/781ba5ad442ee61b7389c3ffa4176cd1dbf5770f))
* context menu doesnt support parameters ([b91d5ec](https://github.com/GZMOS/PurrNet/commit/b91d5ec7b00d2163140578324e497b204fb135a9))
* copy managed types when calling RPCs locally ([28b7091](https://github.com/GZMOS/PurrNet/commit/28b70917a70429f84332b1acefcc82fedf6bf272))
* copy player list for visibility manager ([aa01187](https://github.com/GZMOS/PurrNet/commit/aa01187736551b23aebc379d2017109afb8eeac1))
* Correct push ([a2bbc9b](https://github.com/GZMOS/PurrNet/commit/a2bbc9baa8c852c6bd1492df12c9f45e012da8f5))
* create scene component and save editor hashes for builds ([bad1cb0](https://github.com/GZMOS/PurrNet/commit/bad1cb000344a0e7745f01d38e4231e6c66a6d48))
* custom dela packer for NetworkID? is obsolete now ([353082c](https://github.com/GZMOS/PurrNet/commit/353082cbf300138b5f6d30dfd14d741e93fe3ab1))
* decrease general bandwidth usage ([261a145](https://github.com/GZMOS/PurrNet/commit/261a1450df13e0ce5f0e0fcfaae9f7c16c31903f))
* decrease the delta packer overhead ([0a1b3d7](https://github.com/GZMOS/PurrNet/commit/0a1b3d76a0eb9a7deebc6627b83223f453780be8))
* delay observer events until all visibility is done ([b783d1b](https://github.com/GZMOS/PurrNet/commit/b783d1b007cccfa805f1f84d123ca17f1243398e))
* delay prefab generation one frame after OnValidate to avoid warnings ([f595816](https://github.com/GZMOS/PurrNet/commit/f595816856f7b7ff76c4353fa6b33a1912698ad8))
* delayed observer events would cause issues when it was despawned ([7078c4e](https://github.com/GZMOS/PurrNet/commit/7078c4e37d5f39cda854d8e06b4f1c548bdc3188))
* delta compression issues ([f2d9814](https://github.com/GZMOS/PurrNet/commit/f2d9814fda32249d402c5ac8e7c43064bc13a1e2))
* delta list packing ([7392b4b](https://github.com/GZMOS/PurrNet/commit/7392b4b6e276ae91cde72dd2b3c509194cc1e1ab))
* delta module proper MTU usage instead of hard coded value ([6e9a1ef](https://github.com/GZMOS/PurrNet/commit/6e9a1efd41878cc010930a1ca1ed4be5e2118c6f))
* delta packer for generic System.Object ([479b535](https://github.com/GZMOS/PurrNet/commit/479b5356a4e01273f638c4c38f8b2f5e3ebfe0db))
* delta packing registration was faulty ([3db16b8](https://github.com/GZMOS/PurrNet/commit/3db16b844ee0e4379debed08f50f652506235586))
* delta serializer for basic unity vectors ([3acc4e3](https://github.com/GZMOS/PurrNet/commit/3acc4e31988bda8ac9107153d2e9997456965192))
* despawn event not called, by the time OnDestroy arrives the owner info is wrong ([d0c090e](https://github.com/GZMOS/PurrNet/commit/d0c090ef72980430fe8c237877d07645a4f3261f))
* despawn events not triggering properly in Host mode ([aac86b7](https://github.com/GZMOS/PurrNet/commit/aac86b7b2dd95edb98a336b989f07ef17379b7ae))
* despawn events triggering other despawn events ([c3b75d7](https://github.com/GZMOS/PurrNet/commit/c3b75d7a006bfd1c5c64029e01c6017719d44ba6))
* Destroy with timer not working as intended ([4c2f4ff](https://github.com/GZMOS/PurrNet/commit/4c2f4ff0a38dd3c9f28234c5d3dd71023a67cdef))
* Destroying any component on a spawned object despawns it ([16a7fb3](https://github.com/GZMOS/PurrNet/commit/16a7fb3c7bdf640441f710e13f7bc0a8b2f9a8cc))
* Dictionary pool domain reload safety added ([11c1c68](https://github.com/GZMOS/PurrNet/commit/11c1c68f366e955234e51730b1c35f5dc9d216dd))
* diposable dic packer stuff again ([707fcb8](https://github.com/GZMOS/PurrNet/commit/707fcb86a080c0de3c07a934288b1bf140ae76db))
* Disconnect fixed update from OnTick ([1b06c89](https://github.com/GZMOS/PurrNet/commit/1b06c89fc56274dac17bfdb93a13e57f6ffcd883))
* disposable Array/Hashset collections missing delta packers ([71b1059](https://github.com/GZMOS/PurrNet/commit/71b1059df8a2b1ddc37a31f55128c0fd27d86019))
* disposable dic delta writer ([73b7561](https://github.com/GZMOS/PurrNet/commit/73b75611d35929139324ca707e164e3e7588f3e0))
* disposable dictionary serialization fixes ([0a93474](https://github.com/GZMOS/PurrNet/commit/0a934746bda14c997c2aaaf3948fee32c81f8fd0))
* disposable list leak detection and GC reduction ([02be3c5](https://github.com/GZMOS/PurrNet/commit/02be3c5e8508d8eca16297f9288f9005ec3f8edc))
* disposable list myers action application ([e5c3160](https://github.com/GZMOS/PurrNet/commit/e5c3160641a9bea983eb6ef5548359196066e86a))
* disposable list packer issue ([23598b9](https://github.com/GZMOS/PurrNet/commit/23598b9192b0f0402d1487d7e84644d14b4f97f4))
* disposable list writer doesn't account for null list properly ([bcf1b01](https://github.com/GZMOS/PurrNet/commit/bcf1b015fe4af202a7560ff684db92a7ecb9fb94))
* DisposableArray duplicate ([af07f84](https://github.com/GZMOS/PurrNet/commit/af07f84b4d71d01a665aa797e71e58502ed11be9))
* DisposableList stuff ([ab4eb21](https://github.com/GZMOS/PurrNet/commit/ab4eb2107b08ee196327a52a7398eb78f45f5626))
* dispose bones when destroying object ([f92b774](https://github.com/GZMOS/PurrNet/commit/f92b774e412c93c6c4c051bc23744ad66d84fd8e))
* disposing stuff ([6b74e68](https://github.com/GZMOS/PurrNet/commit/6b74e68801f1ca3667c26504b893482c82c35b63))
* do ownership stuff on early observer added ([e5724c6](https://github.com/GZMOS/PurrNet/commit/e5724c6d37a8c5dab40f6fe5cd21c7570deaa8c1))
* don't auto despawn manually spawned identities ([ca52e95](https://github.com/GZMOS/PurrNet/commit/ca52e951c8de5d59c3684392f0e4e83eb5f6fac5))
* don't block TriggerOnOwnerChanged ([13ccc68](https://github.com/GZMOS/PurrNet/commit/13ccc68d5d5f4c4347dd440bb13271acba6a10bf))
* don't change reflection values when receiving but controller (aka host) ([dcd25ed](https://github.com/GZMOS/PurrNet/commit/dcd25ed01f4da24c09df4b116c74dc9a572b0539))
* don't duplicate network transform entries ([923bad8](https://github.com/GZMOS/PurrNet/commit/923bad81f8b517246c140b8ad0ef1be20b8d45ed))
* Don't modify fixed delta time for real I hope ([1cb4875](https://github.com/GZMOS/PurrNet/commit/1cb4875a1debf886201198e4c3c93cce9e08ad5c))
* don't put `skipSceneAutoSpawning` in the pool ([e84f639](https://github.com/GZMOS/PurrNet/commit/e84f639b77fcea0922f252768de8812bf8f77857))
* don't read optimistic out-of-bounds transform data from clients ([1029322](https://github.com/GZMOS/PurrNet/commit/1029322305afd04673c85703dcbc5c3a06b76e4c))
* don't register multiple times for host ([686a883](https://github.com/GZMOS/PurrNet/commit/686a883e18ec142855a20ef1d6345549658db86a))
* don't send irrelevant data for the NT ([315c331](https://github.com/GZMOS/PurrNet/commit/315c33131f45e9faa94e5047814438290f561869))
* don't sync parameters controller by curves ([9f71243](https://github.com/GZMOS/PurrNet/commit/9f712437584bf2e4920194bad9a9d3aeef7a5a2e))
* dont call asServer false if not client and vise-versa ([e610b3c](https://github.com/GZMOS/PurrNet/commit/e610b3c37692c563d053f4bc137c3ac3e2338746))
* dont check build index manually ([d4329db](https://github.com/GZMOS/PurrNet/commit/d4329db1579174c08f73b64b062e1dd17a8a2dfc))
* dont create a server object until we need it since it causes issues ([801db42](https://github.com/GZMOS/PurrNet/commit/801db425600b41bf6eb0b9fc295edb9d082324b2))
* dont destroy stuff we shouldnt ([ac5521e](https://github.com/GZMOS/PurrNet/commit/ac5521ed1307546ca40e723a89b3f429c60373e1))
* dont hash the id you dumb nut ([f79bc12](https://github.com/GZMOS/PurrNet/commit/f79bc123f8bb22d3ca8a8a2f6089e2990ed9c40a))
* dont include rpc related things here ([f986586](https://github.com/GZMOS/PurrNet/commit/f98658604cdcc4266b52f090ce97c57e50d0ff12))
* dont reconcile hashes that should not be synced ([2fc4c31](https://github.com/GZMOS/PurrNet/commit/2fc4c3179cd5fe1c1cb92df494c190a5f515de1b))
* dont send despawn message on client disconnecting ([904f503](https://github.com/GZMOS/PurrNet/commit/904f5030405b809cc22b4f2e5b2f555ae294c66d))
* dont send empty packets ([91a1832](https://github.com/GZMOS/PurrNet/commit/91a1832c2c632fec64c1d64ab276b16cbeb26a9c))
* dont use static constructor ([0ad1ced](https://github.com/GZMOS/PurrNet/commit/0ad1ced6c34502bcfe91c298b7fe08fb250422ee))
* dont use System.Threading.Tasks.Task.Yield due to webgl ([8c358bb](https://github.com/GZMOS/PurrNet/commit/8c358bb4739aa546a859cf28803553c2070329fb))
* DontPackAttribute only works for field ([5846ecd](https://github.com/GZMOS/PurrNet/commit/5846ecd9a5c4f2d9a07e41361f64e67ac8ddb0ec))
* double download/update bug for big data ([a785e7f](https://github.com/GZMOS/PurrNet/commit/a785e7f7c5652f0452b552964f0bb614b9a7051f))
* double ticks! ([9f74d3e](https://github.com/GZMOS/PurrNet/commit/9f74d3e87d9f1dedbb73829334f527189c17481f))
* Drawing purr buttons in network identity scripts ([20e9267](https://github.com/GZMOS/PurrNet/commit/20e9267729547b248e617bef93988f3f9118952f))
* duplicate sending of scene events causing some issues ([ac05903](https://github.com/GZMOS/PurrNet/commit/ac059036a66cea09f35c874f5498e96e50f5583c))
* dynamically changing region was error prone for the purrtransport ([92e2bb0](https://github.com/GZMOS/PurrNet/commit/92e2bb079091a474b5397327a7efe1e812e62235))
* dynCall is not defined ([536e8c7](https://github.com/GZMOS/PurrNet/commit/536e8c7dcbe44a9fce6c6f9ae29bd484cbe39de6))
* each identity had its own pending owner which cause issues ([3e336fd](https://github.com/GZMOS/PurrNet/commit/3e336fd3cc355dad5786d57bf7a9f829cb2ca2e1))
* easier to read and click stack traces ([4c928bb](https://github.com/GZMOS/PurrNet/commit/4c928bb6a2c84533bed72aaec06a02b5381110e4))
* editor and build mismatch ([2992139](https://github.com/GZMOS/PurrNet/commit/29921392b4fe825f370b1669ac121b69f4f7a847))
* editor events ([a0d6a83](https://github.com/GZMOS/PurrNet/commit/a0d6a83fed02a2de77ad42ed861d7f0439bb6055))
* ensure that it at least replaces with empty method for `ServerOnly` ([9750c5d](https://github.com/GZMOS/PurrNet/commit/9750c5d620e05c10421c0f0578451285d58358eb))
* enum delta packers weren't implemented ([13ed11f](https://github.com/GZMOS/PurrNet/commit/13ed11f922651136ee52b3e7ab09a91c7ca52902))
* error when adding component at runtime ([a7d068c](https://github.com/GZMOS/PurrNet/commit/a7d068c81001e645d49921f9caced42c7eef7840))
* error when owner disconnects and despawn on disconnect is enabled ([c67c7e0](https://github.com/GZMOS/PurrNet/commit/c67c7e0e94a6efef04d8ad2bb43a0a8dc17b5beb))
* evaluate observers before and after full spawn ([9df01ec](https://github.com/GZMOS/PurrNet/commit/9df01ec6200b5c56ec6bc09192ffa77475835479))
* event subscription issue in network manager ([84bfa6e](https://github.com/GZMOS/PurrNet/commit/84bfa6eccc3f426772e40b3808247cd2b54ea00a))
* Expanded the rtt summary ([7668055](https://github.com/GZMOS/PurrNet/commit/766805521bacdba984a15deb9f8011aed71c78c5))
* explicitly get root identity in this critical call ([c4215c9](https://github.com/GZMOS/PurrNet/commit/c4215c96cae035471c3a9c26bf6a0e5e53169703))
* expose a list of PendingSceneOperation on the scenes module ([9642643](https://github.com/GZMOS/PurrNet/commit/9642643ef481304a3937d83016a92353a811c92c))
* expose common used events on the network manager for DX simplicity ([ad7fd85](https://github.com/GZMOS/PurrNet/commit/ad7fd85173a784585248c862a03f26c96317233e))
* expose EvaluateVisibility for easy access ([0731fd7](https://github.com/GZMOS/PurrNet/commit/0731fd7c08e8c9dcffb929d2809f9032c9ae86f2))
* expose latest read data properties for network transform ([65e6a23](https://github.com/GZMOS/PurrNet/commit/65e6a236b5819fec9e74002c886b245014713bbe))
* Extended SyncVar callback to also include old value ([ffee19e](https://github.com/GZMOS/PurrNet/commit/ffee19ec610fb645ff97608bd718d9f854aa6267))
* Extra initializers for old C# versions ([27e2bb4](https://github.com/GZMOS/PurrNet/commit/27e2bb4c3e755ad2d522a607a579c4fe8cb1403e))
* fallback reader for delta didnt use new object serializer ([9541da6](https://github.com/GZMOS/PurrNet/commit/9541da68f9f5d96567578cf439c7be6d650ccbb8))
* filter NT per scene ([4cb0983](https://github.com/GZMOS/PurrNet/commit/4cb0983e606ca95192445dc773c247ea47187faa))
* filter override for network stuff only ([c10fa0c](https://github.com/GZMOS/PurrNet/commit/c10fa0c6b91aa0e5ef037f42b00724c4ca504530))
* filter shouldn't be as broad as a GO ([9c1597f](https://github.com/GZMOS/PurrNet/commit/9c1597faadb2e7c208d1a3e3c2e835ce24e9114b))
* find right mult function ([c7bb95b](https://github.com/GZMOS/PurrNet/commit/c7bb95b20241b6b18f097c8249934938e6c50d49))
* for 6.3+ doesnt make sense to allow for None so lets just ignore it ([7c4bfac](https://github.com/GZMOS/PurrNet/commit/7c4bfacf2313367752120059c3ded04d103f53e2))
* for steam if localhost or local ip just connect to self ([43e9019](https://github.com/GZMOS/PurrNet/commit/43e9019e03e8efa916dc96abaa6d60c0b3fcbb3b))
* force build ([08e26e4](https://github.com/GZMOS/PurrNet/commit/08e26e4dcd1eb72c14eb803e4a915ede3be9bfa8))
* force release for testing ([46e92ab](https://github.com/GZMOS/PurrNet/commit/46e92ab549eb19a8261641f688f1eddc1c27546d))
* forceing release ([43af913](https://github.com/GZMOS/PurrNet/commit/43af913f3051249721557030abafbb926eec2ede))
* forgot it here ([eae97e1](https://github.com/GZMOS/PurrNet/commit/eae97e1b86bbc6eea5d7264ce5f8a48406d1daea))
* forwarding data with delta packer ([135e8df](https://github.com/GZMOS/PurrNet/commit/135e8df2f9147fd2100d1edc71c87d13bb45c265))
* fuck you c# ([d1986df](https://github.com/GZMOS/PurrNet/commit/d1986df12b414c59e1f81861677a7dfffa90b996))
* GC when validating rpcs ([adeca8a](https://github.com/GZMOS/PurrNet/commit/adeca8a34f869cb39e1cd7700ecc2bbee22cb438))
* generic and type inheritance / polymorphism ([5c5edfd](https://github.com/GZMOS/PurrNet/commit/5c5edfd0666e5ef66c3f350d6f47e6b05d7254e5))
* generic RPC bad formated IL ([c090c17](https://github.com/GZMOS/PurrNet/commit/c090c178901f2e8f673d9518f0781a7f6de796c8))
* generic RPCs exception logging ([a6cc5a8](https://github.com/GZMOS/PurrNet/commit/a6cc5a85daf5abeb6641a5eadd8ed48038890e46))
* generic type serialization ([f72b1f1](https://github.com/GZMOS/PurrNet/commit/f72b1f11ab3d5b449f63d8e0c9e3e04062c48bad))
* generic type serialization ([61c59db](https://github.com/GZMOS/PurrNet/commit/61c59db5ea382b0efb37deb096d5d879026df163))
* generics were screwed by some past commits in this version ([d6d5458](https://github.com/GZMOS/PurrNet/commit/d6d5458980ad356de8eb857250e3899a48e08050))
* GetHashCode fails if list is null ([0ab9281](https://github.com/GZMOS/PurrNet/commit/0ab9281b48dce6cb9124dc7a6a601fbb7e7880eb))
* GetLocalPlayer compiler error ([ad13278](https://github.com/GZMOS/PurrNet/commit/ad13278e9ee4ee72f2ea960d0fd022b91b08f5fe))
* GetModule can fail here ([4ea9c5a](https://github.com/GZMOS/PurrNet/commit/4ea9c5ab6133cf14deace076f556649f78619cb6))
* Getting latest transform state before syncing ([6619bbc](https://github.com/GZMOS/PurrNet/commit/6619bbcf5abfd3d2c2985ab93c6fbfbeeae3f910))
* give each thread it's own bit packet pool ([0d5dd6c](https://github.com/GZMOS/PurrNet/commit/0d5dd6c40209cccf593cb880592a2c75a7ae4fb5))
* half quaternion acting up ([9bf5a2c](https://github.com/GZMOS/PurrNet/commit/9bf5a2cdf919eabc8fd64e0ef0906b92d262b19e))
* handle exceptions during tick processing in ServerTick method ([78f1408](https://github.com/GZMOS/PurrNet/commit/78f14086bede0a9411ec507e4f47edd191ad1ee3))
* handle isServer scenario differently ([d7a930e](https://github.com/GZMOS/PurrNet/commit/d7a930e60060ecf1dbbc56159731420c4edcc047))
* handle null selfRef and improve error handling in IEquatable generation ([44166b5](https://github.com/GZMOS/PurrNet/commit/44166b5a6d826afadcbb85c7d7f3af80586c1e54))
* handle the case where Transform is null when packing it ([51cc083](https://github.com/GZMOS/PurrNet/commit/51cc08347ac8da4c3fd361b455a5862f83d2c253))
* has connected owner wasnt tacking scene info account ([fc67e55](https://github.com/GZMOS/PurrNet/commit/fc67e559a6c5dd807bffd44bef33f9ddb45a49fc))
* hash collisions would break delta compression, added type explicitly to avoid this ([bc670ab](https://github.com/GZMOS/PurrNet/commit/bc670ab69682922c31c05fefae40c16f19f4f02f))
* hashset Create and obsolete constructor ([5c35273](https://github.com/GZMOS/PurrNet/commit/5c35273b030333786f95358601fc68cda7609520))
* hide in hierarchy only ([da99e58](https://github.com/GZMOS/PurrNet/commit/da99e58c17221bef61364cb9940159cdf06512c7))
* hierarchy actions needs a rework ([6ce073c](https://github.com/GZMOS/PurrNet/commit/6ce073c6b10cba9dbc3abf579fdfbc83e4059495))
* host disconnecting was despawning other player owned objects due to bad cache ([52e1717](https://github.com/GZMOS/PurrNet/commit/52e171765f8e2b3834ed9ea3178979bbdabfcce4))
* Host migration should be enabled to check if it must force CanSee or not ([5814337](https://github.com/GZMOS/PurrNet/commit/58143374834ae94d908a8d552a6150d45bd45c00))
* Host migration should be enabled to check if it must force scene public or not. ([576e263](https://github.com/GZMOS/PurrNet/commit/576e263412f790af8c161fe1993d2fddbac39ebc))
* I blame valentin ([daffef5](https://github.com/GZMOS/PurrNet/commit/daffef536e906cb717e1ce8e5316b5620e3e345d))
* id contains a scope too, make sure to check for that as well ([c358864](https://github.com/GZMOS/PurrNet/commit/c358864844d3e04e3983dbb430c5ceb8813e59fa))
* IDuplicates of the disposable collections were wrong for managed types ([a11a9be](https://github.com/GZMOS/PurrNet/commit/a11a9be2ca2a249328a21b4cc0376fcd501a7d5e))
* if base index not received on client then wait for it before spawning ([d9915fa](https://github.com/GZMOS/PurrNet/commit/d9915fa5691d24a54a29bad025a1de172d422df3))
* if collection is disposed just return default value ([80cc7a9](https://github.com/GZMOS/PurrNet/commit/80cc7a9e0434d482ee6630641eb00c6a9445db2e))
* if empty list ([d5c77f3](https://github.com/GZMOS/PurrNet/commit/d5c77f3a2d4dcabc145bdb6ef7ca8333ca99ec40))
* if parent type doesn't have a writer, use the specified type one ([e8df49a](https://github.com/GZMOS/PurrNet/commit/e8df49a1296e2082c3368d7fc60d4ccc1d026f2a))
* if pending ownership is present when giving actual, delete the pending request ([a100b96](https://github.com/GZMOS/PurrNet/commit/a100b96825cb58fdd35b06225353b7e51fa72def))
* if server is starting wait for it to fully spin up before starting client ([4bbf0d6](https://github.com/GZMOS/PurrNet/commit/4bbf0d6bc233d7029dec93607991a3ec3850712d))
* if server, always use the ownerServer value ([9626f51](https://github.com/GZMOS/PurrNet/commit/9626f513957ec5db316e27807bc622786820879e))
* if string is null, write null ([047c0c9](https://github.com/GZMOS/PurrNet/commit/047c0c9e5a298410c349c3b88e2c8af83b9687a7))
* if transform from A to B but types mismatch create it from scratch ([2e1799c](https://github.com/GZMOS/PurrNet/commit/2e1799c7bb5a3e4f78459093207f60601db21fa1))
* if we hit cleanup from `OnDisable` force close the connection ([318c5ed](https://github.com/GZMOS/PurrNet/commit/318c5edfd59fe0af734821cdd23c7dadde524b69))
* il error ([5e27ed6](https://github.com/GZMOS/PurrNet/commit/5e27ed62c9bfdfe0d3644e2e6dd5ae14d09018b7))
* il errors ([b52c522](https://github.com/GZMOS/PurrNet/commit/b52c52248b595bf00152334230f5843f328df106))
* il errors ([ba68c30](https://github.com/GZMOS/PurrNet/commit/ba68c30df2c94d70991edb38756ed5bbf2dea60e))
* IL field reference ([6d7654e](https://github.com/GZMOS/PurrNet/commit/6d7654eedc9a435ffab7f420b4d372de9c9b3ab2))
* IL generation for Delta serializers ([75f85f6](https://github.com/GZMOS/PurrNet/commit/75f85f67ab35ec7daa65c183b631f43ea6911304))
* IL generic resolving ([9f04291](https://github.com/GZMOS/PurrNet/commit/9f042912b8628a53384195d30c051f8974fa1af9))
* IL post processing bugs by [@milesizzo](https://github.com/milesizzo) ([6b99be5](https://github.com/GZMOS/PurrNet/commit/6b99be591f32733b4ee7fab4a8d1b8e3565c8897))
* ILPostProcessing error ([39db35b](https://github.com/GZMOS/PurrNet/commit/39db35b247c959fe95842492e9bfe947f1df9184))
* improve network assets reliability and synchronization ([c508cd1](https://github.com/GZMOS/PurrNet/commit/c508cd10b733aa9ae841ad7b9a54712766b1a8fe))
* Improved GC of statistics manager ([1d563a8](https://github.com/GZMOS/PurrNet/commit/1d563a8234393851ac2bdfdac9df6a80250a821a))
* Improved purr buttons to work with inheritance ([d7363bb](https://github.com/GZMOS/PurrNet/commit/d7363bb889d5b75bc99d18ee75ec507f158becce))
* improved some packing for the NT ([e7eab45](https://github.com/GZMOS/PurrNet/commit/e7eab45db557b28cbe57e0f245809173f8697f6d))
* improved statistics manager ([8fed412](https://github.com/GZMOS/PurrNet/commit/8fed412172ffdb88d74d7b80c1d093052f10644c))
* Improved validated syncvar handling ([4fe4434](https://github.com/GZMOS/PurrNet/commit/4fe443447d15264ff6f110fa4e12978b347ad020))
* include a simple version of the started event ([91d7229](https://github.com/GZMOS/PurrNet/commit/91d72298c1740ac22370954250493ae582f184ac))
* include Cache-Control header too ([86badfa](https://github.com/GZMOS/PurrNet/commit/86badfac77a023d7ca67aad322816fdca0ca0f70))
* include full type for generic too ([4990d69](https://github.com/GZMOS/PurrNet/commit/4990d6983b059c20252c9dafd80250c6b93824e0))
* include identity inspector for the state machine ([49c2963](https://github.com/GZMOS/PurrNet/commit/49c29638bb52be094b97dca406904f4e9e103331))
* include local pos for child pieces ([4c67434](https://github.com/GZMOS/PurrNet/commit/4c67434b0fa854943a8ca84073931457b83f639c))
* include purrnet version and color buttons insteasd of showing LEDs ([9612890](https://github.com/GZMOS/PurrNet/commit/9612890fbee45da9f795ef4574894c25f9dcbefe))
* include scale with the spawn packet ([56479c6](https://github.com/GZMOS/PurrNet/commit/56479c638679ef481d17b28dc0e2d8c857b69ca8))
* include sceneid for spawn point provider ([2c64600](https://github.com/GZMOS/PurrNet/commit/2c646000874c61432005038c39ed9fe13543e7cd))
* initialize ping history size and stats on server connection ([5f8689a](https://github.com/GZMOS/PurrNet/commit/5f8689ae9ae785e2764725bb7f53a0c6ed018aa2))
* initializers for older C# versions ([9df4c8b](https://github.com/GZMOS/PurrNet/commit/9df4c8bfbc3d9aa907334b4f983c5fcdbef9f7dc))
* Instance handler clearing done properly ([c065727](https://github.com/GZMOS/PurrNet/commit/c06572785a09f60ec9bcc7e828fa9c8a9fdd4112))
* Instantiate with pos and scene was behaving weird with character controllers ([fc13e2f](https://github.com/GZMOS/PurrNet/commit/fc13e2fd0a4a9388a08404fbe780dda791af684d))
* instantiate with pos, rot and scene ([1cc9003](https://github.com/GZMOS/PurrNet/commit/1cc90039d413798226a61f7e2957fb69bc547b3c))
* instead of throwing an exception lets just log what happened ([4a1c0b8](https://github.com/GZMOS/PurrNet/commit/4a1c0b8047d9ca08016a6f028a35cdf639a16483))
* interfaces and IL post processing ([299325a](https://github.com/GZMOS/PurrNet/commit/299325a1b40f4283b3d6ccc84f60d81ad4777d11))
* interpolation attempt to not jitter on buffer resize ([f4ef145](https://github.com/GZMOS/PurrNet/commit/f4ef145302fa8c28a15151e07b99a23d9d1d6660))
* interpolation code refactor ([032bd60](https://github.com/GZMOS/PurrNet/commit/032bd60ad86c69d251e623d7dfa9b252df1db358))
* introduce `SetDirty` for syncvars ([dcd8f86](https://github.com/GZMOS/PurrNet/commit/dcd8f86d22a451d4128b5d3b5661e9a19e568c04))
* introduce disposable dictionary delta packers ([086c701](https://github.com/GZMOS/PurrNet/commit/086c701df10263ce47423aaf4b8aa20b023d8f51))
* introduce HalfQuaternion ([a174729](https://github.com/GZMOS/PurrNet/commit/a17472912a0b68ad64267a4f35da45b9178a05b5))
* introduce LateLateUpdate for nt ([86c3d87](https://github.com/GZMOS/PurrNet/commit/86c3d87e49fce11e572261df6cbd6c22c8ec06d2))
* introduce on earlyObserver added callback ([b8d53b0](https://github.com/GZMOS/PurrNet/commit/b8d53b05416c967d3c3a07769c8bcda11b1cddbb))
* introduce the `Create(capacity)` variant for DisposableList ([4d1fab3](https://github.com/GZMOS/PurrNet/commit/4d1fab33107353af379cae204924f2c59795bdf7))
* introduce timing mode for NT ([3cbd38f](https://github.com/GZMOS/PurrNet/commit/3cbd38fd681673e39701ad999dc9dc5a592b2a53))
* introduced DontPack attribute ([2fea79e](https://github.com/GZMOS/PurrNet/commit/2fea79e8cc8e2598001e29ab73b51fe4feaf7eb9))
* InvokeLocal wasn't reseting position properly all the time ([66c8536](https://github.com/GZMOS/PurrNet/commit/66c8536ecde5bba6010104f7039cfab3e528a48c))
* is host returning true when it shouldnt ([12fb371](https://github.com/GZMOS/PurrNet/commit/12fb371003359ad035cfe88a66fcb14f63684528))
* isOwner returns false in OnDespawn() ([0827023](https://github.com/GZMOS/PurrNet/commit/08270234c952b809492858a1247e343a4490e0b5))
* issue with the hierarchy builder ([b6620a9](https://github.com/GZMOS/PurrNet/commit/b6620a97d6dff45af6ec8232082d0cc79af07217))
* just dont process NuGetForUnity ([0140920](https://github.com/GZMOS/PurrNet/commit/0140920a19800fe4512210fdfb1f79e2660f35b3))
* LastNID patch, this needs to be reworked ([16dc6d3](https://github.com/GZMOS/PurrNet/commit/16dc6d30cec6c85eb8fad123be0a3bfee2299a5a))
* leak checker; removing some GC for rpcs ([3578dcf](https://github.com/GZMOS/PurrNet/commit/3578dcf1e6faee1a5c3eca086f406b15065fa98a))
* let it do the tickening or something ([3a15fb0](https://github.com/GZMOS/PurrNet/commit/3a15fb075d334413475f3b2f7d8af94f384f4953))
* link the changlog ([9ef043a](https://github.com/GZMOS/PurrNet/commit/9ef043a70732867218d4aaf98f0d2e7c0c38fbf0))
* Linked network prefabs logic added ([ba4a2e4](https://github.com/GZMOS/PurrNet/commit/ba4a2e4c4fa2e60e0d86ed7d8e6f3609fded1662))
* load original scene when cleaning up ([d7603ff](https://github.com/GZMOS/PurrNet/commit/d7603ff449b86ce3023c8e609651527a5996f6a8))
* local space network transform issues ([559a115](https://github.com/GZMOS/PurrNet/commit/559a11535887b46a83a5544286dbe84b766e97dd))
* local transport ([697c3a4](https://github.com/GZMOS/PurrNet/commit/697c3a46270e53bde8da0f0dc1b0eb7d4fdfef74))
* local transport ([c746154](https://github.com/GZMOS/PurrNet/commit/c746154e8e7e9bae43803d8808a4aaec18dc7a63))
* log error when trying to change parent to a different scene ([8fd7b40](https://github.com/GZMOS/PurrNet/commit/8fd7b409976d7ed0fbbc5c227ba043dce34462a8))
* make `DontPack` attribute skip creating generators entirely if at the class level ([e18bf9c](https://github.com/GZMOS/PurrNet/commit/e18bf9c2e77658f33e9d8b1756dffd050306e33e))
* make a contributors display to put our community forward ([0b121ee](https://github.com/GZMOS/PurrNet/commit/0b121eed9da309e84a1b224d78c31e454ad12efd))
* make core unity dependencies optional ([12b06e1](https://github.com/GZMOS/PurrNet/commit/12b06e191792bb7d1c7416621c2c500af044f935))
* make network prefabs more robust ([7654456](https://github.com/GZMOS/PurrNet/commit/7654456b4c49b95753890f005348a1829996cb85))
* make network transform buffer dynamic ([f27426c](https://github.com/GZMOS/PurrNet/commit/f27426c5c7219dd110f15bd90ab39b7cc60c6aee))
* make network transform less fragile by including a networkid (more data but we need to refactor this at some point anyways) ([b79d02d](https://github.com/GZMOS/PurrNet/commit/b79d02d8b7ea2c4f9d150f506ed7627516f1c9ce))
* make observer attributes inherit from PreserveAttribute ([2e9fcf9](https://github.com/GZMOS/PurrNet/commit/2e9fcf912749095dbd23731f21e265bd7449ed07))
* make ownership clearer for InterlatedWithDispose ([2615cc7](https://github.com/GZMOS/PurrNet/commit/2615cc7944bbed4d4c13ec25f2e77cc480ccc3bb))
* make parent changes checks more robust ([2f212aa](https://github.com/GZMOS/PurrNet/commit/2f212aa6aa8d9316d2a8ebb4096dcb47112682c2))
* make rpc calls work properly under coroutines ([09272d1](https://github.com/GZMOS/PurrNet/commit/09272d15b8b6a267a8eef06340c7fd9560740210))
* make sure `OnTick` exceptions don't have side effects to other subscribers ([8811429](https://github.com/GZMOS/PurrNet/commit/88114295949b5b64936c0becb7d5854f2714a744))
* make sure any exceptions in the callbacks for synctypes dont break any flow AND that reset pool resets it's internal state ([de2a64c](https://github.com/GZMOS/PurrNet/commit/de2a64c216207ee4b495edab85fd253e3e3634f1))
* make sure big data doesn't mix with old big data (unreliable) ([cabc112](https://github.com/GZMOS/PurrNet/commit/cabc1126c88f47b11659c309448efe6441bd5ef9))
* make sure client has the isSpawned boolean set to true ([568e256](https://github.com/GZMOS/PurrNet/commit/568e2563be49450e2339bfd61b7f10fd25cde4f4))
* make sure disposable list is registered for dictionary ([34e461a](https://github.com/GZMOS/PurrNet/commit/34e461a6adea9617d29f41c740bda036a84b677e))
* make sure gui is enabled for purrbuttons ([4462d78](https://github.com/GZMOS/PurrNet/commit/4462d78054792329c498b2dc49e859782267c5f4))
* make sure not to unload if only one scene left ([dc67398](https://github.com/GZMOS/PurrNet/commit/dc67398f949b7a2647a8581ff66e1ab2bbe9d66f))
* make sure scene is valid when unloading ([0d7e8a3](https://github.com/GZMOS/PurrNet/commit/0d7e8a324237d8eec7faa8c7f3c0c0a743ff0553))
* make sure scene matches since this can change ([39e71d6](https://github.com/GZMOS/PurrNet/commit/39e71d6366326f2eef59aa0a1a59e814505ec567))
* make sure the packer has the proper data when communicating to others ([dc9f03c](https://github.com/GZMOS/PurrNet/commit/dc9f03cd39b184cd261016f187bd72a872a0e44e))
* make sure to apply the changed value ([83822be](https://github.com/GZMOS/PurrNet/commit/83822be32cef9a66ff712268291734ad2030e2d9))
* make sure transport runs before default mono behaviours ([3e7f11e](https://github.com/GZMOS/PurrNet/commit/3e7f11e44485a57e3aa21f1e15ac4c9c2213c42d))
* make sure we account for local client being gone when handling ticks ([ca622e5](https://github.com/GZMOS/PurrNet/commit/ca622e51372b387b4f687d577341bfcd5f2223a5))
* make sure we don't create something that is already registered ([78a6907](https://github.com/GZMOS/PurrNet/commit/78a69075603bf4248681989dbeba00edd0176898))
* make sure we dont add duplicates ([ae5d159](https://github.com/GZMOS/PurrNet/commit/ae5d1594fb31b8b3b3193ffd22a95822fca9dcb2))
* make syncvar change existing value instead of creating a new one ([e9a7336](https://github.com/GZMOS/PurrNet/commit/e9a7336e1d8ecdb36c2ba420113158ee20eeb9eb))
* make UDP timeout configurable ([489db80](https://github.com/GZMOS/PurrNet/commit/489db80dcf50622b79001747bbe12866270ff3fc))
* make unified pool and fix host disconnect cleanup issues ([c99d4b0](https://github.com/GZMOS/PurrNet/commit/c99d4b0458092ec750528546b07aa2a81e52f126))
* make UnloadScene return an async operation ([c4f1723](https://github.com/GZMOS/PurrNet/commit/c4f172309400ab4580dea442849c294d2ae8783e))
* manual spawn API patches ([c034518](https://github.com/GZMOS/PurrNet/commit/c0345181bb5b6b54bd8d964c7ad7ca09fdaee1c5))
* mark manual spawns such that we handle them differently (like not populating observers automatically) ([2dc77b8](https://github.com/GZMOS/PurrNet/commit/2dc77b8d8bc358902a5d407ddd8dcc5475de91f6))
* Merge pull request [#153](https://github.com/GZMOS/PurrNet/issues/153) from bookdude13/HasModule-Client-Fix ([b531534](https://github.com/GZMOS/PurrNet/commit/b5315344a4778626b039ea22fe7823bd9e74b834))
* merging of additions was too aggressive ([77549fe](https://github.com/GZMOS/PurrNet/commit/77549fe8a30873b638a717110dd7d5a6a4ef81ac))
* messaging issue ([2a8c0cb](https://github.com/GZMOS/PurrNet/commit/2a8c0cbf684636d617f85a4eae141e0f0e52b305))
* metadata file for CHANGELOG.md ([dd139fc](https://github.com/GZMOS/PurrNet/commit/dd139fc066987c8942d8751d6f194a917fa9616c))
* method reference was wrong when creating getters/setters ([cebdaa9](https://github.com/GZMOS/PurrNet/commit/cebdaa9528fdfcd3b10d6fc5eaa371e6b5c841ad))
* method reference was wrong when creating getters/setters ([1388543](https://github.com/GZMOS/PurrNet/commit/13885438311808700dd595183ba7ff066f8756aa))
* migrate old `NetworkOwnershipToggle` data ([35eeb4f](https://github.com/GZMOS/PurrNet/commit/35eeb4f73150cb566fd642d534f3128946c8c5a9))
* milesizzo aka [@mgi](https://github.com/mgi) on discord rework on prefab provider interface ([8ba42f7](https://github.com/GZMOS/PurrNet/commit/8ba42f7cbfbce898acd622392e18003c7d1c4b3f))
* miss-wording in the PurrTransportInspector ([9774c15](https://github.com/GZMOS/PurrNet/commit/9774c150aad6f48fc60de03d47d9294dc01aa341))
* missing using ([0f51df2](https://github.com/GZMOS/PurrNet/commit/0f51df2921e55dc28c483d4efe444267dc14fab5))
* mistake ([685b9e9](https://github.com/GZMOS/PurrNet/commit/685b9e95b16c54ec8420b5f14f95b034568a6bd4))
* modules registering and unregistering ([f10d22e](https://github.com/GZMOS/PurrNet/commit/f10d22e5fdae4c4f23505550cc55ccdb85598af1))
* more inclusive pre-processor defines ([dc1a4dd](https://github.com/GZMOS/PurrNet/commit/dc1a4ddf3f7378668d888a9c5b5a52a01b77c4b8))
* more modifier delta packing fixes ([c604c50](https://github.com/GZMOS/PurrNet/commit/c604c50fd946ff5460fe63e7921e455885ea42eb))
* more nuget tests ([a6d144d](https://github.com/GZMOS/PurrNet/commit/a6d144ddee1795ccc94d36fceb346746b956dfee))
* more optimizations ([7e777de](https://github.com/GZMOS/PurrNet/commit/7e777ded30420e69a00fb6f7a020d6a3893baa0a))
* more precision for angles ([e481bc9](https://github.com/GZMOS/PurrNet/commit/e481bc905af0f320dd4d78e9ee338978b7ee1374))
* more purr transport tweaks ([a6da989](https://github.com/GZMOS/PurrNet/commit/a6da9895d9f511fb00566d4afaaa0cadbb562498))
* more raycast types for rollback module ([975ab10](https://github.com/GZMOS/PurrNet/commit/975ab103da67a36097f36517ec6255e96f9f6a83))
* more robust register calling and skipping of assemblies that don't refrence the purrnet assembly ([5daec62](https://github.com/GZMOS/PurrNet/commit/5daec625ecb0c3cb405162afc2bcdb772f170d81))
* more test ([2b237cd](https://github.com/GZMOS/PurrNet/commit/2b237cde9c67e4f60b4c5415c11d8b811d331566))
* move retry logic to purrtransport api level ([5d209a8](https://github.com/GZMOS/PurrNet/commit/5d209a8942838cbc797a3fa6e0bb85baaefc2759))
* MTU overflow bug ([2036d21](https://github.com/GZMOS/PurrNet/commit/2036d21b4bfd3c67ece85ba38d958c5508d72d7e))
* myers diff GC fixes and some tests for consistency ([6fc9525](https://github.com/GZMOS/PurrNet/commit/6fc95257d3aa4d0c2651b68e39e0a17d2bca8583))
* myers impl ([61b1929](https://github.com/GZMOS/PurrNet/commit/61b19299f17a9d48377a7ad73791c039a63bda45))
* naive delta packer for array and list ([4904eaa](https://github.com/GZMOS/PurrNet/commit/4904eaa69a2b5540b4d81b1a3fdf6000806a77f3))
* network animator logic for owner auth ([e69c326](https://github.com/GZMOS/PurrNet/commit/e69c32613e2c502e817baac00dc85215a27596e4))
* Network Asset also pull base class assets ([89b0d56](https://github.com/GZMOS/PurrNet/commit/89b0d567db0e02c35ff7d2a9e1b6a6705f584847))
* Network asset exclude editor namespace ([11b45f6](https://github.com/GZMOS/PurrNet/commit/11b45f67388ada773138a21c6e830a38cd20cf08))
* Network assets post asset processing proper push ([b383377](https://github.com/GZMOS/PurrNet/commit/b3833779d77bbc2ab3b23e78960c7cebd53db359))
* Network assets pull multiple sub assets ([de49d8b](https://github.com/GZMOS/PurrNet/commit/de49d8b07fdabb9057336bfef4317c806e7d6357))
* Network Assets working with Sub-assets ([769ff32](https://github.com/GZMOS/PurrNet/commit/769ff32e111da0315d6c077c0e1c8e41902a8900))
* network prefabs invalid caching ([b90c66e](https://github.com/GZMOS/PurrNet/commit/b90c66e960e2914b96fbb33b973c5957eb445a47))
* network reflection and network assets ([1adea71](https://github.com/GZMOS/PurrNet/commit/1adea71cf4a1517122a5130429500a4a99ece8fa))
* network rule to allow target rpcs to target server ([e8339c3](https://github.com/GZMOS/PurrNet/commit/e8339c3b8b301ff4a2520fbb00554afc9752fc85))
* network transform delta compression ([c080a09](https://github.com/GZMOS/PurrNet/commit/c080a0965867072ace69b0265361576654a79d6a))
* network transform gathers current state at the same time it also updates view for others ([1508ad6](https://github.com/GZMOS/PurrNet/commit/1508ad60da9e2ba8d32591fe6d30782ae11e5d54))
* network transform jitteriness ([e102a28](https://github.com/GZMOS/PurrNet/commit/e102a28385989947fdd76e0a160e54ae70a24fe6))
* network transform module bug ([b6a0a5d](https://github.com/GZMOS/PurrNet/commit/b6a0a5d747f0369d9316123b8619e376f97572e8))
* network transform not updating latest position if not controller properly ([c64fe4f](https://github.com/GZMOS/PurrNet/commit/c64fe4fe16fd7305441aafe1fe0c43a1e2b43f21))
* network transform order is deterministic now ([0fca613](https://github.com/GZMOS/PurrNet/commit/0fca613d55987eec86cfbd9fb18b33f9b47981ad))
* network transform ownership change weirdness ([5af65e4](https://github.com/GZMOS/PurrNet/commit/5af65e409fe2fca4510a5a0136464ebbb61043f6))
* network transform underlying buffer miss-match ([e16c830](https://github.com/GZMOS/PurrNet/commit/e16c8304f53a3c66ced94c542d56f33dbe1c4fba))
* network transform, instantly register local spawns ([744023d](https://github.com/GZMOS/PurrNet/commit/744023d18577342cb13858ea0611c9ae4b800865))
* NetworkAnimator exclude sender on observer rpc ([f775555](https://github.com/GZMOS/PurrNet/commit/f775555735bd990da09eb7a4b74d411d4d32dcc2))
* NetworkAnimator not behaving properly when also server ([1069944](https://github.com/GZMOS/PurrNet/commit/10699440d31334986cb2a517566104f39889ab49))
* NetworkAssetsEditor and null assets ([c30cc95](https://github.com/GZMOS/PurrNet/commit/c30cc95decee22b1cbd4825b77584b55725ece1a))
* NetworkBones adjustments ([5a61a86](https://github.com/GZMOS/PurrNet/commit/5a61a869a55c057609b05773acc9594908ea433c))
* networkbones courtesy of Resolute Games ([896a018](https://github.com/GZMOS/PurrNet/commit/896a01876af0d372c6b3723004e1e78bf99fa9e3))
* NetworkBones.cs cached ID was not being reset when packet was split ([96dcb34](https://github.com/GZMOS/PurrNet/commit/96dcb34613ea279b65d1a6d9f098aca8c62d9460))
* networkRules being accessed when despawned gives error ([d98aea9](https://github.com/GZMOS/PurrNet/commit/d98aea963f0fd1a91d50590998d96066fbb2ce44))
* NetworkTransform `ForceSync` was weird ([c2593fd](https://github.com/GZMOS/PurrNet/commit/c2593fdb3b3b8a019f851302df1016a711386a9f))
* NetworkTransform rigidbody 2d patch ([a1280fe](https://github.com/GZMOS/PurrNet/commit/a1280fe60da012f9046e3bbbaa6b2869faef323e))
* new myers deltalist packer ([f8c6d05](https://github.com/GZMOS/PurrNet/commit/f8c6d058e8c192e3355fe7d768a2ef32825e0ca9))
* no flags for build ([96c8a40](https://github.com/GZMOS/PurrNet/commit/96c8a40bb7610a4b90538f861d5f453713396717))
* Null check in SyncDictionary editor code ([ef31ec6](https://github.com/GZMOS/PurrNet/commit/ef31ec67aa1607532cf4af0889b5b2711711d690))
* null exception for non ValueType syncvars ([fbec9e5](https://github.com/GZMOS/PurrNet/commit/fbec9e585e7c78d4503f5cfcba04a6602d387a41))
* observer events need to flush RPCs for the onspawned to be processed correctly ([67cf99f](https://github.com/GZMOS/PurrNet/commit/67cf99f2da195a3da29a9d33698b95d85f3361e1))
* observer rpc runs twice when runLocally true on host ([637dd98](https://github.com/GZMOS/PurrNet/commit/637dd98fda8dc7198026f2b09360fa13e5a62e9b))
* obsolete code in unity 6.3+ ([0c404b6](https://github.com/GZMOS/PurrNet/commit/0c404b6e40e736890fde757ce1bbad79048d2a87))
* odin inspector support ([6e072bd](https://github.com/GZMOS/PurrNet/commit/6e072bd2f6f3b516887c3ae43971e1567c28fcff))
* old value was wrong for dic delta packer ([539c760](https://github.com/GZMOS/PurrNet/commit/539c7607415c493a881e0d676c5f90d068cd41f8))
* on spawned as client in host not being called ([3b62546](https://github.com/GZMOS/PurrNet/commit/3b62546ef15d3889bf805a883427c421fe068e51))
* only adapt outside of editor ([cacc0c7](https://github.com/GZMOS/PurrNet/commit/cacc0c7bc9d501ee7bbbf65d1e1a028145182612))
* only add scene if valid ([fc84643](https://github.com/GZMOS/PurrNet/commit/fc84643082e318af95d12a7a5811c750f5fe5fda))
* only connect client once server has fully started ([7d2ec28](https://github.com/GZMOS/PurrNet/commit/7d2ec288b7a37c92b0441c89142792280b34b8ae))
* only keep latest `SetX` for animation ([badec0d](https://github.com/GZMOS/PurrNet/commit/badec0dd5b6f56b88085f4e1ea6195ff4a3d33cf))
* only return true for "isOwner" if client is active ([39666eb](https://github.com/GZMOS/PurrNet/commit/39666eb2db3ef63ad15bca53eb7319c96335c2ea))
* only set pending owner during auto spawn if one isn't already set ([41948c4](https://github.com/GZMOS/PurrNet/commit/41948c49995d8cf071f675bbc08028ebb515fd68))
* only trigger if holding a prefab ([05e37d1](https://github.com/GZMOS/PurrNet/commit/05e37d117b41ec9157934a1f93d0367c33f1c195))
* only trigger owner changed once fully spawned ([35645a9](https://github.com/GZMOS/PurrNet/commit/35645a993a5f2268ab8f1c5959220b6984d234aa))
* onObserverAdded not being triggered for client spawned prefabs ([1a1b41f](https://github.com/GZMOS/PurrNet/commit/1a1b41fb018f7134225b5e1f4efb64dd41263def))
* OnOwnerReconnected and OnOwnerDisconnected not always firing ([d1bab76](https://github.com/GZMOS/PurrNet/commit/d1bab7680c5ab412a116b472aaece7d2d3d62eb8))
* onSentData added to SyncInput ([a3271ae](https://github.com/GZMOS/PurrNet/commit/a3271aeff32827602684d766c8239f2f30287f72))
* oopsie daisie ([83b7668](https://github.com/GZMOS/PurrNet/commit/83b7668f954e14cfedb16da720bdde76bb736592))
* optimized PurrScene attribute ([eee752c](https://github.com/GZMOS/PurrNet/commit/eee752ca78d13a18ee297a1efe2e8aa3bdffb389))
* order of add was screwed by my previous attempt to be smart ([315dd96](https://github.com/GZMOS/PurrNet/commit/315dd966ef007c8e08e355aeefb58f6e77415ba5))
* owner connected event not being fired ([b6ba2be](https://github.com/GZMOS/PurrNet/commit/b6ba2bef4135f6b5d894b716843fcfc52d98eff9))
* ownership change callback order ([fa9ea10](https://github.com/GZMOS/PurrNet/commit/fa9ea10ed9777023af3ee6da61f409639357ae55))
* ownership events ([9a245f9](https://github.com/GZMOS/PurrNet/commit/9a245f9c7dd4a9a70da9daa2fd27c57db84b711f))
* ownership execution order ([ac63487](https://github.com/GZMOS/PurrNet/commit/ac634872e1ff2454e16ff812b7bf060b9eb50176))
* pack unity LayerMask ([a23e8dd](https://github.com/GZMOS/PurrNet/commit/a23e8ddc64b88ed17142b63eb07449f89f88ef1a))
* packer caching problems ([878e7b9](https://github.com/GZMOS/PurrNet/commit/878e7b94b0389ec37b115b6c60f96ccc31a4f266))
* packer crashing issue due to bad argument handling for method invocation ([a376d3c](https://github.com/GZMOS/PurrNet/commit/a376d3cf5600ea0d1717c42c6b0eb84c3fc7bae5))
* packer duplicate mistake; tests for hasher ([b66ca76](https://github.com/GZMOS/PurrNet/commit/b66ca76f528746e4aa126d90948482ec26e481e6))
* Packer handling of unspawned gameobjects and transforms ([cc68315](https://github.com/GZMOS/PurrNet/commit/cc6831536deabda40ee8f7cce69d204692ab78fb))
* packer rework ([9630787](https://github.com/GZMOS/PurrNet/commit/9630787b9ba57066fd59cf84673d777d2ef756db))
* packer was rounding floats for CompressedFloat when it doesn't have to ([e968119](https://github.com/GZMOS/PurrNet/commit/e9681190fd2e6dcb1b91378757d8e6ce548d1942))
* packing for DisposableArray.cs ([1e5b4d2](https://github.com/GZMOS/PurrNet/commit/1e5b4d20907f09d45c03459c7396f8005dd12c7c))
* parenting was broken from previous NT rework ([9536c5d](https://github.com/GZMOS/PurrNet/commit/9536c5d180b7e7f22ccce5c7b452128c091434c0))
* pass asServer parameter to TickManager in NetworkManager and RawNetManager ([0c288b3](https://github.com/GZMOS/PurrNet/commit/0c288b332028caf1f937014201f78ec9f5e8d688))
* pass connection to authenticator ([76b40ff](https://github.com/GZMOS/PurrNet/commit/76b40ffd543f0dc50ef8351d2e50150bd0ff5276))
* patch lingering process and bad hash reconstruction bug ([52764b9](https://github.com/GZMOS/PurrNet/commit/52764b9c188c8f60b622da0224252b02080b4ca4))
* patch lingering process and bad hash reconstruction bug ([bd56f3d](https://github.com/GZMOS/PurrNet/commit/bd56f3d6b17a7250f88e1b56a433cdfc441ecbbe))
* patch local execution flag issue ([96ca28a](https://github.com/GZMOS/PurrNet/commit/96ca28a26249a9a105a15276a6e12382eb35613f))
* Performance optimized network manager inspector ([1d52b21](https://github.com/GZMOS/PurrNet/commit/1d52b213a194d5f67174d0653d6a858cbdb3b92f))
* Pickle resizing was causing issues, making sure it won't have to resize ([754dc65](https://github.com/GZMOS/PurrNet/commit/754dc659559ac8963ef8fcb63a52b0b91682899a))
* ping calculations ([cd7cfd7](https://github.com/GZMOS/PurrNet/commit/cd7cfd70c1427c0d58dfe5e3601dd58ff79d2cb8))
* Player Identity AOT safety ([520b268](https://github.com/GZMOS/PurrNet/commit/520b2682d214ff97e1a5b922f6fe40ad13ef3a9a))
* PlayerIdentity<T> catchup in OnSpawned when it happens at a later stage ([53305a4](https://github.com/GZMOS/PurrNet/commit/53305a468694272e151bb1bbc7e6901cc8bb3f02))
* players list throws error when not ready ([9450ca4](https://github.com/GZMOS/PurrNet/commit/9450ca442a519ad90f8554dcb71c7980f8506f8b))
* pool stuff should be relative to parent ([1697c4a](https://github.com/GZMOS/PurrNet/commit/1697c4a438186f34b5da3e32eb2e7301aa9efdc7))
* pooled array not being cleared broke some functions ([70b8121](https://github.com/GZMOS/PurrNet/commit/70b81214b858a2ac3d4917c1fa3d7eadd9a548f2))
* pools being in the wrong scenes ([062f546](https://github.com/GZMOS/PurrNet/commit/062f5460393e5c13f6ba4bac92492f22dc225f9f))
* populate local player id as soon as server has it ([7fddf9d](https://github.com/GZMOS/PurrNet/commit/7fddf9dde5de0b03edd729ce3fb021b97c69567d))
* possible fix for network reflection buld ([3bbf58e](https://github.com/GZMOS/PurrNet/commit/3bbf58e46da52d62add19f4fe10e78ad72052c85))
* preserve localPlayerForced ([a8a9257](https://github.com/GZMOS/PurrNet/commit/a8a9257955c4527f501bdf1b5cbab7647ec3534c))
* preserve localPlayerForced ([6487666](https://github.com/GZMOS/PurrNet/commit/6487666bbc530e2c297d065b3c6552183c207027))
* preserve order for reliable ticks ([1b81323](https://github.com/GZMOS/PurrNet/commit/1b813238310baffb3eb79a983b8ede5ccbab866d))
* prevent destruction of objects in editor mode and cancel if it was already destroyed ([5351ba5](https://github.com/GZMOS/PurrNet/commit/5351ba52cd4d0569c9c1c05b349f83dd583a6351))
* previous undo compiler errors ([a889ac8](https://github.com/GZMOS/PurrNet/commit/a889ac894ce77a344491395b154c604e54beb1ef))
* profiler rpc data preview ([b4b908a](https://github.com/GZMOS/PurrNet/commit/b4b908afdef46bf4f1697db74fc2e8c0d0028fa0))
* profiler/statistics locked to editor only ([d8272f7](https://github.com/GZMOS/PurrNet/commit/d8272f747871778d69a68de5ea7943dc93769121))
* profiling window for network usage ([2ee6514](https://github.com/GZMOS/PurrNet/commit/2ee65146f15bf8a67567148ea7437f302f982d8e))
* proper `currentlyLoadedCount` calculation ([c8c784f](https://github.com/GZMOS/PurrNet/commit/c8c784feba3b9371b4727d581e0870b1afb47889))
* proper comparer ([a30043c](https://github.com/GZMOS/PurrNet/commit/a30043c802391a2b98ad65502e93d1012f7edef8))
* proper purrnet package name ([1c56ade](https://github.com/GZMOS/PurrNet/commit/1c56ade439a29b3468461708fafba9d2326307b7))
* proper unsubscribe ([0a081f5](https://github.com/GZMOS/PurrNet/commit/0a081f51458b5e3137c89d42efb62efa2c69a595))
* properly call despawn() even if not fully spawned ([0b1739f](https://github.com/GZMOS/PurrNet/commit/0b1739feb6aba7e3eccef27d410898c4a435aeaf))
* properly filter players when forwarding rpcs ([3b05719](https://github.com/GZMOS/PurrNet/commit/3b057195282236c496337875544d938946ce4731))
* properly handle multi drag editor events ([b365616](https://github.com/GZMOS/PurrNet/commit/b3656166d02580d87446f80a97c39caab2c64cfe))
* properly lerp NetworkTransform localScale with parent changes ([9f67be5](https://github.com/GZMOS/PurrNet/commit/9f67be523c6468cba17b73d3c6df0b3b9446101c))
* properly populate RPCInfo for runlocally ([bd99145](https://github.com/GZMOS/PurrNet/commit/bd991450479f1b09bff4e2be463e9cfd8c9b567a))
* properly register interfaces and collections of interfaces ([dd0b28e](https://github.com/GZMOS/PurrNet/commit/dd0b28edb6f5c0ea06415eba4c9ee1ed577764a5))
* properly set cleanup stage when in host for the other module ([6903e7c](https://github.com/GZMOS/PurrNet/commit/6903e7c379d750616356279ddd1a4415965c892a))
* properly set scene as dirty ([15476e8](https://github.com/GZMOS/PurrNet/commit/15476e826b6a986dc51a1a9448b80e6a770b9943))
* purr transport errors ([19450bd](https://github.com/GZMOS/PurrNet/commit/19450bd95c7b82afe0b211c5ca337a6f0876fd6a))
* PURR_LEAKS_CHECK for dictionaries too ([3c1b8d6](https://github.com/GZMOS/PurrNet/commit/3c1b8d652685969fe82f5a2341a6073d75ac3414))
* PurrNetSceneProcessor error when Resources folder is missing ([e72a465](https://github.com/GZMOS/PurrNet/commit/e72a465ce3ffb5219cafe37ed904ab7a7b07b2d5))
* purrTransport also uses web sockets, fixing same issue here ([b9c7f51](https://github.com/GZMOS/PurrNet/commit/b9c7f51b503eefcaa7f4f4cd5e069f07c58f50c7))
* PurrTransport cache made changing master server a pain ([f642aff](https://github.com/GZMOS/PurrNet/commit/f642aff74b635176dcb1036b2a54f5909f42874b))
* purrtransport compiler error ([62713c5](https://github.com/GZMOS/PurrNet/commit/62713c51f0b792fc762a91725cdc469d00924c75))
* PurrTransport notification saying that it is meant for dev purposes only ([a26a541](https://github.com/GZMOS/PurrNet/commit/a26a54108add2a521d1cef3c60e665b25c68560e))
* push `IsRegistered` ([b72a193](https://github.com/GZMOS/PurrNet/commit/b72a1931cf3cbe922a058c0bfd41cb4a58cae197))
* Quaternion equality check is stupid ([69b1c32](https://github.com/GZMOS/PurrNet/commit/69b1c32d766429307ce0d2b277c45994c53fd838))
* Quick stupid fix ([8804efe](https://github.com/GZMOS/PurrNet/commit/8804efed49cc42de997b7dc66f2923d64dde4bd1))
* re-eval visibility right away when parent changes ([73afec7](https://github.com/GZMOS/PurrNet/commit/73afec781818187f828bcceba00c29bef614b2f0))
* reconcile animator state on owner switching ([56c1b91](https://github.com/GZMOS/PurrNet/commit/56c1b91f0ee454042aa5ddfecf29a6e0536fe6f7))
* records ([983728a](https://github.com/GZMOS/PurrNet/commit/983728a6befc13b375ae4b8e5bbde8ed63c2cdbe))
* refactor player spawner to simplify it ([a0f1269](https://github.com/GZMOS/PurrNet/commit/a0f12697939023636c3336285a4752ed951b4fb2))
* refactoring `AreEqual` helpers for the packer ([20b2c70](https://github.com/GZMOS/PurrNet/commit/20b2c70665be9960e6df05776ebe261e53a45c7b))
* reference type for pooling and keeping references ([9b174f0](https://github.com/GZMOS/PurrNet/commit/9b174f029f4aded2c90f21f1ee76fcaebcc56d3c))
* reflection getmethod failing ([568db6c](https://github.com/GZMOS/PurrNet/commit/568db6c39656b5aa315756127740db3c3b1a9f95))
* register functions were being stripped ([63e0b5f](https://github.com/GZMOS/PurrNet/commit/63e0b5fa8e21f516ea15b2c7eae30c7ae1ccb9bc))
* release ([4da2001](https://github.com/GZMOS/PurrNet/commit/4da2001c46b5df1ab24708a289a8fc4cb06c9a7b))
* remove cheetah data from being on by default ([1e3e7a5](https://github.com/GZMOS/PurrNet/commit/1e3e7a5c6621bce77c93d6ef1fd147f81f9c56aa))
* remove commented-out code in PlayModePatch ([d1f866c](https://github.com/GZMOS/PurrNet/commit/d1f866c2a0c640764578a12c99b0a6241741492e))
* remove obsolete code ([20c381a](https://github.com/GZMOS/PurrNet/commit/20c381a564b35c93507147a6b42f0ff325341ae1))
* remove Octokit.Net dependency ([5331439](https://github.com/GZMOS/PurrNet/commit/5331439bceafa8e8b2faf4907cc7cbfd6f6bd7ac))
* remove readonly from ApplyTo method ([b3a0d13](https://github.com/GZMOS/PurrNet/commit/b3a0d131c731061a3c284caeb76ca03b4384fe8e))
* remove steamworks dependency on util code ([20af4f5](https://github.com/GZMOS/PurrNet/commit/20af4f52a47669606370642a1d938fe36e59d84a))
* Remove sync tick fail log ([555be9b](https://github.com/GZMOS/PurrNet/commit/555be9b039b35153060d26c2886498ba61905e23))
* remove UniTask as a dependency ([725cabf](https://github.com/GZMOS/PurrNet/commit/725cabfc54a037375e94fb16ccbcb2e1d94aead7))
* Removed locking of fixed delta time ([ba09d1d](https://github.com/GZMOS/PurrNet/commit/ba09d1d9d4333f55eca14c281353241175bb3fd6))
* removed unnecessary unsubscribe from broadcasting setup ([ca17934](https://github.com/GZMOS/PurrNet/commit/ca17934ee90f0e59ea071254d45ef494bfd81a37))
* removing ownership bug ([995e891](https://github.com/GZMOS/PurrNet/commit/995e8917840e751b7048148fa06d21bd7488851f))
* rename rollback methods and further test them ([5f10efd](https://github.com/GZMOS/PurrNet/commit/5f10efd7fa8f4e2ce3694cc755d4e03202bd69b1))
* replaced UDP transport ([101f649](https://github.com/GZMOS/PurrNet/commit/101f6496fb572997dea2dcf2b563c225c5e66e60))
* requireOwnership rule wasn't being checked when receiving RPCs ([1f56433](https://github.com/GZMOS/PurrNet/commit/1f5643317703c4fed5d2f9d112b2bd92e530c395))
* resolve hostname for the udp transport ([cc86356](https://github.com/GZMOS/PurrNet/commit/cc86356a681413e26bab57564c49d45dd6a8808d))
* retry for purr transport if first fails ([8330de0](https://github.com/GZMOS/PurrNet/commit/8330de02f989757d0d10c6855dce717c3166a90c))
* return value of ValueModifier wasnt necessary ([ed0d668](https://github.com/GZMOS/PurrNet/commit/ed0d668b7ade468ca9024527d7bae92a2c5980d0))
* revert ([f6ffe42](https://github.com/GZMOS/PurrNet/commit/f6ffe42e384224b925167df4f18c853cbd4c9bd3))
* revert UDP transport ([04b6625](https://github.com/GZMOS/PurrNet/commit/04b66257498ed829db42505d3687123ec9d9a153))
* reverted bad changes ([94914f4](https://github.com/GZMOS/PurrNet/commit/94914f4b907105abf1f4646551d61210c706eff4))
* rework how RPC are called ([0f3c4f1](https://github.com/GZMOS/PurrNet/commit/0f3c4f1cfe992a89ca719afe53fd6e167c840d72))
* rigidbody moving weirdly if pooled ([5cc8524](https://github.com/GZMOS/PurrNet/commit/5cc85245aabcb458a5b793eb6f1cde9b64424565))
* robust identity events (exceptions won't break flow) ([0a820d1](https://github.com/GZMOS/PurrNet/commit/0a820d1814435a0d09ffd3e67c184cfab81e7adc))
* rollback some transport tests ([ef7b239](https://github.com/GZMOS/PurrNet/commit/ef7b2398352d9033e3bab16df890b1dd864618d4))
* rpc batching and ownership ([bd35c80](https://github.com/GZMOS/PurrNet/commit/bd35c80434a2c1de288fd1a75b504f77ae4e010d))
* RPC buffering bug with scene objects ([eec3e67](https://github.com/GZMOS/PurrNet/commit/eec3e67b86484d0c2e835bfc8c0268dd86bfb6a0))
* rpc not found issue ([afdf69c](https://github.com/GZMOS/PurrNet/commit/afdf69c4b6dca70f84d4e8e1617d99034819705a))
* RPC signature was referencing wrong localPlayer in NetworkModules ([a9f3dd8](https://github.com/GZMOS/PurrNet/commit/a9f3dd82b2551ad0a9a3b199625e94eadeb3bad7))
* RPC signature was referencing wrong localPlayer in NetworkModules ([fb40ee1](https://github.com/GZMOS/PurrNet/commit/fb40ee1c0c08f26afd8b2d2b1c4837b67fdb8fca))
* rpc stuff? ([3568eef](https://github.com/GZMOS/PurrNet/commit/3568eefd210428d9f16d41f279e40c67e9d98143))
* Safeguard state changes ([881d215](https://github.com/GZMOS/PurrNet/commit/881d2150c03feff69e0b9d5941e11f77745893cd))
* Safeguarding sync types ([8a34c82](https://github.com/GZMOS/PurrNet/commit/8a34c821bf14625f9d1ebc18167651f2a2976c46))
* Scaling hard correction threshold of Network RB ([39d5cb2](https://github.com/GZMOS/PurrNet/commit/39d5cb21b1ffba2c0db9e2210eabfe40b5fec525))
* scene cleanup logic ([558505a](https://github.com/GZMOS/PurrNet/commit/558505a50b70fe05b9e166093200058dabfa9d6c))
* scene errors ([dd54ba6](https://github.com/GZMOS/PurrNet/commit/dd54ba637156c902df9bc9dce060a9ca43407757))
* scene initialization timing ([cbd4b02](https://github.com/GZMOS/PurrNet/commit/cbd4b02fb23be578bf7d96f26fdd8a294db7456c))
* scene load events ([63dbc5c](https://github.com/GZMOS/PurrNet/commit/63dbc5cbbc306c9175230b37209a98c3397cc07c))
* scene loading issues ([fed6dcb](https://github.com/GZMOS/PurrNet/commit/fed6dcb10d67f9ef8dbcc30c1049852d0c76ff48))
* scene loading observer issues on host ([8487e94](https://github.com/GZMOS/PurrNet/commit/8487e945464346944a5efddd16595b13aaa31941))
* scene module cleanup ([8c6177d](https://github.com/GZMOS/PurrNet/commit/8c6177de62c785b76cb742a976524cf98bae7d70))
* Scene objects spawn issue for HOST ([6cf0b02](https://github.com/GZMOS/PurrNet/commit/6cf0b0209b02fb50f20e5d2f1f926f5d99c56a15))
* scene pooling not clearing properly ([1acdd22](https://github.com/GZMOS/PurrNet/commit/1acdd222acf5fc86b1ac3ef7b01d507f3b9137e7))
* scene stuff ([469a70d](https://github.com/GZMOS/PurrNet/commit/469a70dacb2ebae3acebaf2a270e56e16d0d04fe))
* sceneLoaded for host error isn't an issue, events are triggered when ready ([73bf266](https://github.com/GZMOS/PurrNet/commit/73bf266b027e29b2407f81f2a55a6e58015404c1))
* send latest state if not owner auth on NT ([1a6aef5](https://github.com/GZMOS/PurrNet/commit/1a6aef546c9043f51dca5cc050df2654e9401004))
* send parent change on event instead of delaying it further ([7e03bd6](https://github.com/GZMOS/PurrNet/commit/7e03bd6b03137d38533eb52a646efd0dbbcbb870))
* send pending owner until hierarchy v2 is out ([04f37f0](https://github.com/GZMOS/PurrNet/commit/04f37f05a2e5965850e24e7b4a802af8d1e75759))
* send RPCs on despawn events and syncvar forces last value update ([da3f59d](https://github.com/GZMOS/PurrNet/commit/da3f59de4a4a79b21663d53c56b89e4dc4ff230a))
* senderId in RPCInfo was wrong ([a03686a](https://github.com/GZMOS/PurrNet/commit/a03686a74cca61519949ab62bb9e86f0b83f8c5f))
* separate transport update into `ReceiveMessages` and `SendMessages` ([c5909b8](https://github.com/GZMOS/PurrNet/commit/c5909b8174030b9ead46375cdf9c5b45f8bee5d8))
* serialize stacks and queues ([5a19c2c](https://github.com/GZMOS/PurrNet/commit/5a19c2c5fa9cd2e7881e8f5de876f60ec2b8bd6e))
* serializer issues ([574d201](https://github.com/GZMOS/PurrNet/commit/574d201e3d90558a24aa52978dcc2ddc24e60609))
* server only spawn events being wobbly ([2b1cbe6](https://github.com/GZMOS/PurrNet/commit/2b1cbe64591227f47cd085d7a43cb719f081a38f))
* server overriding client position in network transform ([9ce93a4](https://github.com/GZMOS/PurrNet/commit/9ce93a4d2272cfff1635dd12981a84353a7237ed))
* server rpc's on server should not use the network ([06b6d9d](https://github.com/GZMOS/PurrNet/commit/06b6d9d15a78c7b908367af60ffea1e1137b9115))
* server RPCs being called twice on host ([b261cc5](https://github.com/GZMOS/PurrNet/commit/b261cc5e718de2302640c06beed6d48df8973eba))
* Server Stats added to statistics manager ([37a49ec](https://github.com/GZMOS/PurrNet/commit/37a49ec0279393a6a5330d6407f1f57fdc8d286c))
* server+client auto start breaks if only client succeeds; clearer stack trace when RPC body throws ([d7c2ed5](https://github.com/GZMOS/PurrNet/commit/d7c2ed52d80b03dd5fe1e8ee6a87a228ff4d2c79))
* set position after parenting ([f7d4dbf](https://github.com/GZMOS/PurrNet/commit/f7d4dbfdc7fe8a17f8dd9a15cf2a5393f0cac25a))
* set target frame rate to tick rate for server builds ([b1fc358](https://github.com/GZMOS/PurrNet/commit/b1fc35896b66e2ea69f13910962e1a82199787c7))
* setting parent to root ([d37d741](https://github.com/GZMOS/PurrNet/commit/d37d741c24611cf5a61dfd3cef45c9cb78967c3e))
* short circuit was causing invalid IL ([93fad3a](https://github.com/GZMOS/PurrNet/commit/93fad3a11d3afb5082ee7aab3d8f55df025e730b))
* simplify float delta packing, old packer just added more overhead ([3d2b0f0](https://github.com/GZMOS/PurrNet/commit/3d2b0f0e773621ac2744190a0a898511f722679d))
* simplify generic logic ([2a48bf3](https://github.com/GZMOS/PurrNet/commit/2a48bf37b8af52891d69508af835e46d29951dee))
* simplify NetworkReflection.cs code to use object type instead of streams ([a4a8828](https://github.com/GZMOS/PurrNet/commit/a4a8828b4102cb8c5275686dd565f1dd21db9e78))
* simplify packing to avoid stressing serializer for auth system ([3417cfa](https://github.com/GZMOS/PurrNet/commit/3417cfad400d86127db8d81c56ac8a4cbab36660))
* skip certain types without warnings ([6664408](https://github.com/GZMOS/PurrNet/commit/66644083d370ede755517165d24a969a8d483764))
* skip deep processing of certain assemblies ([6fe1411](https://github.com/GZMOS/PurrNet/commit/6fe1411d39b54221f168a80f26b335e9e5153063))
* skip whole numbers ([dbd72ff](https://github.com/GZMOS/PurrNet/commit/dbd72ff79b17835f13e05742c2eff456c320e8be))
* small packets gave division by zero for fossil deltas compression ([0ddd32b](https://github.com/GZMOS/PurrNet/commit/0ddd32b3a38d9c99135a6c5e5148832d729b8a1d))
* Smooth rotational syncing on Network RB ([a4ca898](https://github.com/GZMOS/PurrNet/commit/a4ca898e5372365cf26f3c30ec968c1c6354891c))
* smoother network transform parent changes ([b2c9709](https://github.com/GZMOS/PurrNet/commit/b2c97098bb60d7580e2474e7878b0ace93642e93))
* Solve inconsistency in sync timing ([59e7993](https://github.com/GZMOS/PurrNet/commit/59e7993fbdf64b5cee42e32b19593766e6d377bc))
* some changing parent issues with the network transform ([bee40e1](https://github.com/GZMOS/PurrNet/commit/bee40e15f3adeeb3d34983a70386a5fd7a94d4d0))
* some delta packing for spawn packet batches ([20388f8](https://github.com/GZMOS/PurrNet/commit/20388f8ef8d4c38c66d3a8fd09e008a4647a1499))
* some host issues with visibility rules ([29476a0](https://github.com/GZMOS/PurrNet/commit/29476a08b5626e1e5b84cb69c6498ab52af084db))
* some mismatch on owner changed for NT ([aba3ef5](https://github.com/GZMOS/PurrNet/commit/aba3ef58c99b1c9d146882a94f8a3a57e48ae675))
* some missed cases for dispose here ([1e751ee](https://github.com/GZMOS/PurrNet/commit/1e751ee8c278f6b936fa5ef713027c4ccd817d14))
* some more `try catch` and reset whitelist/blacklist state when pool reset called ([5d4958b](https://github.com/GZMOS/PurrNet/commit/5d4958b1bfe948c7c77431740ce15912c92a1a08))
* some network transform ordering issues ([cb4eed1](https://github.com/GZMOS/PurrNet/commit/cb4eed1355797d0990244447aacb221eba8f20aa))
* some ownership miss-alignment ([63164cd](https://github.com/GZMOS/PurrNet/commit/63164cd89aa0d2ffaf83dd3d7f36ceef8ef70520))
* some packing bugs ([fc9899a](https://github.com/GZMOS/PurrNet/commit/fc9899a1fc8fe9aad5e24aca35b5c429346572b9))
* some packing issues ([f43ee8a](https://github.com/GZMOS/PurrNet/commit/f43ee8a8220c908fda10f94bad6d78ed9c0967ca))
* some safety when cleaning client state ([f7011af](https://github.com/GZMOS/PurrNet/commit/f7011afaba24e90fdf80eb90a0509a274c68d1f4))
* some serialization intricacies ([d8973f9](https://github.com/GZMOS/PurrNet/commit/d8973f9d0833793bb153c0fe69cd634c2c0c00e4))
* some syncvar stuff ([3c3e90e](https://github.com/GZMOS/PurrNet/commit/3c3e90edebe91b826e2df59aa37ba0fbc8c50f58))
* sort by name and resolve same name with hash ([6c0c167](https://github.com/GZMOS/PurrNet/commit/6c0c1675de97f9ce1272c904b33ab36bd2fa0c48))
* sort via GUID instead of creation date ([bec3643](https://github.com/GZMOS/PurrNet/commit/bec364388399993406c9a8afb5af6658074139b5))
* sorting was backwards ([880a670](https://github.com/GZMOS/PurrNet/commit/880a67069036afed16757245dc6fdf2a58b336d8))
* spawn event not being called ([1852845](https://github.com/GZMOS/PurrNet/commit/1852845ed953d15d5e749fa4d6d6ee2522ae3a4d))
* spawn in correct scene ([521bf1b](https://github.com/GZMOS/PurrNet/commit/521bf1b7d5908f07194c88d893130aefd0b53ea7))
* spawn point provider pattern ([7e20abe](https://github.com/GZMOS/PurrNet/commit/7e20abe1a733511572bb231044dcc4985f38027f))
* spawn pos being overriden by unity ([f41f191](https://github.com/GZMOS/PurrNet/commit/f41f1911e3d38be73176e46fbdd4d9652033bcdf))
* spawner issue ([fe48ba7](https://github.com/GZMOS/PurrNet/commit/fe48ba72e93fae342ca3e6115bb4740359deb8d9))
* spawning and despawning on the same frame causes purrnet to freakout ([2b780dc](https://github.com/GZMOS/PurrNet/commit/2b780dca16f8f6db78e390cc8cd2ae9f9b564409))
* spawning concurrency bug ([6af756e](https://github.com/GZMOS/PurrNet/commit/6af756ecaead37b8e73a40326dd591fe055af0c7))
* spawning duplicates on client ([22c7867](https://github.com/GZMOS/PurrNet/commit/22c78676d2847d495072a2b0b22fa9aaf623dbe0))
* spawning with InstantiateParameters ([ab922f6](https://github.com/GZMOS/PurrNet/commit/ab922f66562aaf562ca792adb72ce672e31010d1))
* start server/client, stop server/client always calls the network manager and does it through it instead of individually, otherwise things are unpredictable ([157d47c](https://github.com/GZMOS/PurrNet/commit/157d47cd8405893fd0180b9621f58fc3e6da788b))
* State machine double enter and exit fix ([1b5fbc8](https://github.com/GZMOS/PurrNet/commit/1b5fbc8b5a51ad6fa4ebf56711a8cd8b24b22cb5))
* State machine dynamic index handling ([a61fc01](https://github.com/GZMOS/PurrNet/commit/a61fc017091d690f42da1dbbdc8f0d8a89596c7c))
* state machine editor issues in prefab runtime ([d0ad04a](https://github.com/GZMOS/PurrNet/commit/d0ad04a033fe5e0d860cdd11a6d1cd9be8a16c46))
* State machine exit on despawn ([9884c58](https://github.com/GZMOS/PurrNet/commit/9884c585b1aa8950b56fbc7db82d58d1039bc864))
* State machine host fix ([15bfbcd](https://github.com/GZMOS/PurrNet/commit/15bfbcd1a912ad2a9bceb50dbbb3d8468005595d))
* State machine on change callback order fix ([86081f2](https://github.com/GZMOS/PurrNet/commit/86081f275835a4aecfa9eb48ef39f09e899751cb))
* State machine owner auth custom inspector fix ([e8ce4a4](https://github.com/GZMOS/PurrNet/commit/e8ce4a429806410c17b9f964e24e418032d32f65))
* State machine owner auth initialization ([878c87d](https://github.com/GZMOS/PurrNet/commit/878c87d339f16416a38c3cf16ce536c86f42bcb0))
* State machine queuing for improved consistency ([4fc3fab](https://github.com/GZMOS/PurrNet/commit/4fc3fabca158ec7e82fb109acf5b37db1488b0a3))
* State machine reconciliation + asServer fixes ([04eb69e](https://github.com/GZMOS/PurrNet/commit/04eb69e379c369a3b8be3773224ea8fcf99f70bc))
* static delta rpcs ([77ce2b4](https://github.com/GZMOS/PurrNet/commit/77ce2b4815eb474ef4bb308799c0d3cfafe4209f))
* static rpcs in monobehaviours ([e22044a](https://github.com/GZMOS/PurrNet/commit/e22044a2e9b246f50188a15fc10979484ddae4fe))
* Statistics for steam transport ([c1c16ff](https://github.com/GZMOS/PurrNet/commit/c1c16fff1692dd56c0db009e468ac87970d11adf))
* Statistics manager fix ([4334c69](https://github.com/GZMOS/PurrNet/commit/4334c69eed3ce0d28ffc30822a9830c5e9c9e692))
* Statistics Manager fix ([98b89ae](https://github.com/GZMOS/PurrNet/commit/98b89ae29477b832a3c9aa31712c7a25b66df458))
* Statistics manager improvements ([f494ce9](https://github.com/GZMOS/PurrNet/commit/f494ce96b947ea8a69d049ed50adc39ab4432ac6))
* Statistics manager jitter ([0c5d611](https://github.com/GZMOS/PurrNet/commit/0c5d611b215a5d049c3494c58c189b3b5c4ff8b9))
* Statistics manager versioning position fix ([1539c54](https://github.com/GZMOS/PurrNet/commit/1539c54a46659045893b82665387e52c6bfaca51))
* steam complains on WebGL ([688b095](https://github.com/GZMOS/PurrNet/commit/688b0959b88fa14d857b2396b3615b1503242862))
* steam complains on WebGL ([b9cd7fc](https://github.com/GZMOS/PurrNet/commit/b9cd7fc5284f0a369a4492e3ba35651a055d9468))
* steam errors when trying to use connection after closed ([ee4ed6a](https://github.com/GZMOS/PurrNet/commit/ee4ed6ab6c540d85aaf51ed0f5af29d43508e3ce))
* steam server not properly cleaning internal state ([af3a793](https://github.com/GZMOS/PurrNet/commit/af3a7932271bf7547e8d14bfc23a26e539aa3445))
* still cull rpcs when player isnt observer for other channels ([69c2677](https://github.com/GZMOS/PurrNet/commit/69c26771081843302480415c118fe556351b8cf7))
* still prefer to call empty constructor instead of always initializing it to 0 ([5c667ac](https://github.com/GZMOS/PurrNet/commit/5c667ace880ba56b0a7b2aeb01066fcb60330fe0))
* Stop network transform from forcing RB sleep when not spawned ([94d8527](https://github.com/GZMOS/PurrNet/commit/94d8527d8e54be1afe7717294df1dc5cae113e72))
* stopping steam server didn't properly close existing client connections ([ea36cb5](https://github.com/GZMOS/PurrNet/commit/ea36cb5e883ab159fd2866ab5f12c4ca8638a84f))
* swap buffers to avoid editing collection while iterating ([4d8f45a](https://github.com/GZMOS/PurrNet/commit/4d8f45afdb645026156c34483531c350fcddc3e5))
* Sync Array earlier initialization ([2739995](https://github.com/GZMOS/PurrNet/commit/2739995b200a51c94c329eabd878821a06657393))
* Sync Array Fixes ([63761e9](https://github.com/GZMOS/PurrNet/commit/63761e9a3649d45e94786867c9f72fa86d3be7b3))
* Sync Array send on tick ([5909b18](https://github.com/GZMOS/PurrNet/commit/5909b18580429ceef9622ddff1f6bd001d1df758))
* Sync Array Serialized resizing ([5b3f1b0](https://github.com/GZMOS/PurrNet/commit/5b3f1b0c344df775a0e6549ddc1a3532444b1c26))
* Sync dictionary depending on tick ([a350928](https://github.com/GZMOS/PurrNet/commit/a350928e70fefcb112534d10579220492d9e0d73))
* Sync dictionary sending for clients ([88ce60a](https://github.com/GZMOS/PurrNet/commit/88ce60a2f56e5d594a9f2c54b055eaef8790d4b9))
* Sync dictionary serialization upgrade ([5934acb](https://github.com/GZMOS/PurrNet/commit/5934acbf9a52511353941040a1643728d658a75f))
* Sync List null handling issue ([5704d83](https://github.com/GZMOS/PurrNet/commit/5704d83add83ced6dbab7138cfb3ea0d8f09fe8e))
* sync list on late observer added ([b34fcd0](https://github.com/GZMOS/PurrNet/commit/b34fcd016064afd0ddbded2ad200eea5861239c5))
* Sync types for strict rules ([7722477](https://github.com/GZMOS/PurrNet/commit/7722477cba75fc22b49c6b23af70d4e4b5d57132))
* SyncArray receivers were queuing operations ([0d199da](https://github.com/GZMOS/PurrNet/commit/0d199da7573b988f123cffae58402fb855f80b0c))
* SyncBigData.cs now supports owner auth and switching ([80a6932](https://github.com/GZMOS/PurrNet/commit/80a693297d4ea51d38f2633a6d4fc1b408d0c266))
* SyncList owner auth fix ([19d044f](https://github.com/GZMOS/PurrNet/commit/19d044f76d1af20cf91da1dcb92cb5ee63e8920d))
* SyncList send on tick ([27972c7](https://github.com/GZMOS/PurrNet/commit/27972c7c66c1a6ae7c0785f0924a31f8aa8d15d1))
* SyncList shoudl not queue pending on received ([eec88ba](https://github.com/GZMOS/PurrNet/commit/eec88babe6d71fef68606f638f2ade434c38d3c0))
* SyncTimer fix from Valentins mistake ([afd5d42](https://github.com/GZMOS/PurrNet/commit/afd5d42ab9897e1fcb87e646e6d8057d198ea461))
* SyncTimer issues ([4d8e7f4](https://github.com/GZMOS/PurrNet/commit/4d8e7f4fc00fb9429eecb74727f73206d0d1350b))
* syncvar cleanup and optimisations ([6eae47b](https://github.com/GZMOS/PurrNet/commit/6eae47b1d5b5e25d5b667ec17af77e1a2f05f3d9))
* SyncVar drawer for nested generic serialization added ([bff4e85](https://github.com/GZMOS/PurrNet/commit/bff4e853c07666bf9365705709c753f3fc6bb549))
* syncvar equality check regression ([d280ed5](https://github.com/GZMOS/PurrNet/commit/d280ed58bb1109a4f4622ff393271edc4da4e9ed))
* syncvar events should be triggered when Packer.Transform is successful ([abbc918](https://github.com/GZMOS/PurrNet/commit/abbc918f033be615831efdcc2aa61835707c71d7))
* syncvar invalidate is controller earlier ([2444436](https://github.com/GZMOS/PurrNet/commit/2444436aded533525be1e33930dab28a367e8cc2))
* syncvar issue when ownerAuth ([cc96997](https://github.com/GZMOS/PurrNet/commit/cc96997efc94e95e3efb6f68c0db6bcf01aab75f))
* syncvar let client decide instead of server for ownerauth stuff ([5b4cb65](https://github.com/GZMOS/PurrNet/commit/5b4cb65e423378f28e6e228832a5e2d3a18ea73a))
* syncvar stopping to work after x time ([2bb95c8](https://github.com/GZMOS/PurrNet/commit/2bb95c8261f7e5519d8f372c968f2926cb0bfa85))
* target RPC to host's client failed, this was a regression ([6fdce17](https://github.com/GZMOS/PurrNet/commit/6fdce17673990deb758bd2d4b2348a00ad16c9dd))
* TargetRPC was broken for client to client comms ([fe177af](https://github.com/GZMOS/PurrNet/commit/fe177af40c5e720d2971ebc0b24f00fb5e961ce3))
* testing CI ([d3583a4](https://github.com/GZMOS/PurrNet/commit/d3583a489bc55e245a7bdb1482e45d392f719677))
* testing NetworkBones component ([7bcd4d5](https://github.com/GZMOS/PurrNet/commit/7bcd4d598cfca35fbfd19d569fd3ebe2cfdfe40b))
* this interferes with server shutdown ([b250122](https://github.com/GZMOS/PurrNet/commit/b2501225e3fbdfb6d4bca4cd0265bef4bb0f5e9a))
* tolerance only decides if it's dirt from rest ([c2551c5](https://github.com/GZMOS/PurrNet/commit/c2551c57981bcdaaa31dd9f29952eb8c61bd4440))
* trigger OnEarlySpawn when catching up ([9443c97](https://github.com/GZMOS/PurrNet/commit/9443c97afb637a497bbbc3e0ed11b8d1993f2f73))
* triggering on scene loaded too preemptively ([2171deb](https://github.com/GZMOS/PurrNet/commit/2171deb5ad486653bac16b3850021c0610f4b164))
* TriggerSpawnEvent might have been called multiple times ([95659c3](https://github.com/GZMOS/PurrNet/commit/95659c33de7c690ce18c457b96efce5591078ca9))
* try catch silently the CallAllRegisters reflection step ([091b425](https://github.com/GZMOS/PurrNet/commit/091b425a73caf0f0e94fecb70a391ce0ddbaccbe))
* try to be more careful with errors here ([3beb8d5](https://github.com/GZMOS/PurrNet/commit/3beb8d548a90d3ab5f2d9b3d7644f7eeacaaa624))
* try to match static rpc behaviour to normal rpcs ([267f5c5](https://github.com/GZMOS/PurrNet/commit/267f5c57e0d5d9efe6908a0d6ec1284c565d84d2))
* trying to fix nuget package issues ([bbf83d6](https://github.com/GZMOS/PurrNet/commit/bbf83d699cb9c800dd709c97b560cbcaefd575b6))
* trying to make DDOL deterministic ([d03647c](https://github.com/GZMOS/PurrNet/commit/d03647c71602ef588c165bf86854be9bf7f5efad))
* trying to open non prefab gameobject ([cedb91b](https://github.com/GZMOS/PurrNet/commit/cedb91bcaae27aedb0c41c0f7522c7ad42b91369))
* tuples were breaking code stripping ([2ec1406](https://github.com/GZMOS/PurrNet/commit/2ec14060bafb072877facca8b3949d475d292f1c))
* type error deeper error message ([06904ef](https://github.com/GZMOS/PurrNet/commit/06904ef17432e96fb2ea86705a0bfcbf3f173e46))
* udp disconnect reason ([0156338](https://github.com/GZMOS/PurrNet/commit/01563389b31084fbd69370e784839cbc92b61fa2))
* UDP transport reconnection ([c15c6a5](https://github.com/GZMOS/PurrNet/commit/c15c6a5704c4fc83f990de0b42533fae77b7fb3c))
* ulong delta packer ([01445ae](https://github.com/GZMOS/PurrNet/commit/01445ae5c0cd1ae2147337a6ee7d8eb90a4f51a0))
* ulong packing ([c48f735](https://github.com/GZMOS/PurrNet/commit/c48f7354620cf346eba2ffa95ed529b93fb99517))
* undo early client id setting as it was incorrect ([285268b](https://github.com/GZMOS/PurrNet/commit/285268b7390c2d9c9affe09dd04180f4b1fcb3b2))
* undo last attempt at fixing bug since it created more edge cases ([ef9699f](https://github.com/GZMOS/PurrNet/commit/ef9699f532e51cd30a6b20915ac7f16be71bcdd6))
* undo mess ([9f0f26c](https://github.com/GZMOS/PurrNet/commit/9f0f26c336b16ec78d6f340dd529286cf5c05fad))
* undo serialization order of base type ([d8c8560](https://github.com/GZMOS/PurrNet/commit/d8c85601e8f5f886a24d999e453c1c8bc5732e3f))
* unit tests circucal reference issue ([c9c0624](https://github.com/GZMOS/PurrNet/commit/c9c0624dc3715c6ed3f95b957b05c79046344fb2))
* unity 6 color thingy ([4344e4e](https://github.com/GZMOS/PurrNet/commit/4344e4e3026351967944c00c19a98c5fac29d3aa))
* unity 6.3 toolbar fixes ([7619b8e](https://github.com/GZMOS/PurrNet/commit/7619b8e462d860258c733d3d83b008dee033369b))
* unity version issues ([f8c90e2](https://github.com/GZMOS/PurrNet/commit/f8c90e2ddcd22c1a2a0dc94c427b9041619d1205))
* UnityProxy fails if manager doesn't have prefab provider ([fd2e674](https://github.com/GZMOS/PurrNet/commit/fd2e6746c5c4798430c55c4e41d1ee1fe3806a04))
* unityProxyType being null caused IL issues ([15a85cd](https://github.com/GZMOS/PurrNet/commit/15a85cd3b10ec0865965ad5fa190a68467879f3c))
* unload after the load request ([d339419](https://github.com/GZMOS/PurrNet/commit/d339419e90ee8a80f99a108cbb9a7ba0c9d59bec))
* unload event ([8cf362a](https://github.com/GZMOS/PurrNet/commit/8cf362a44790329892eddf29a87c685e77aa77ac))
* use AssemblyQualifiedName instead of FullName ([a0afe55](https://github.com/GZMOS/PurrNet/commit/a0afe5570a4a5b4669086b749978e8a362174530))
* use fallback writer/reader for types that aren't registered ([9d3cc9f](https://github.com/GZMOS/PurrNet/commit/9d3cc9ff48ad7c187b807c7994ac3389edcb4177))
* use HierarchyV2.SetLocalPosAndRot instead of dup logic ([3732cbd](https://github.com/GZMOS/PurrNet/commit/3732cbdeef22c4d295233cee973bbb498584834c))
* use unscaledDeltaTime for NetworkTransform.cs ([77c23c9](https://github.com/GZMOS/PurrNet/commit/77c23c9bfc3279c435cc665e4db9f4bd2fae9172))
* useDeltaPacking for rpcs, still WIP ([1611074](https://github.com/GZMOS/PurrNet/commit/16110740d3f9ae7829b70b90880ebc2aa4947b91))
* using after free ([890a29c](https://github.com/GZMOS/PurrNet/commit/890a29cd72bf96a70665da77c37aca34caaaa187))
* Valentin you monkey ([386d08d](https://github.com/GZMOS/PurrNet/commit/386d08dd6e984a051e7ca79673c7ddbfe7e93adf))
* Vector2/3Int serializer ([7c3b834](https://github.com/GZMOS/PurrNet/commit/7c3b83440745e7bb073a8ac07613c80000c6c57d))
* version mismatch issue editor/build ([2ebe5a8](https://github.com/GZMOS/PurrNet/commit/2ebe5a8d841a3499fa9cb540ca1079f0fda48b4b))
* web transport weirdness (caused network transform bizzardness) ([04ae8e3](https://github.com/GZMOS/PurrNet/commit/04ae8e35c05ea3f964af8d9c4da38f1aa1136a68))
* webgl builds ([4dccfa5](https://github.com/GZMOS/PurrNet/commit/4dccfa56f567a24f881b14585082a0eb29113bc7))
* weird ownership order ([634ed88](https://github.com/GZMOS/PurrNet/commit/634ed88a8098049f9455cda503b0f5eb7cf7a96e))
* when adding connection make sure it's a new ID ([a61f451](https://github.com/GZMOS/PurrNet/commit/a61f4511b519e0d65af8e57b63973358c92e3bfd))
* when host instantiates with 'Default Owner: Spawner If Client' make sure client gets ownership ([c790a33](https://github.com/GZMOS/PurrNet/commit/c790a33acc6bb9000ff20bf3a619c4db8a14a7cc))
* when interpolation buffer is full, remove up until min buffer size ([adcc020](https://github.com/GZMOS/PurrNet/commit/adcc02032fff1e3e045821a95ffdf3604a958f30))
* when playmode window layout changes it clears the wrapped GUI... ([49b5554](https://github.com/GZMOS/PurrNet/commit/49b555430b7c06c9b026c54f2df617ef3db0e665))
* when sending a target rpc to local player just call it locally ([2982811](https://github.com/GZMOS/PurrNet/commit/2982811a01626b4f0cdf0da0378c5c25a26aa2ff))
* whitelist dirty wasn't being executed ([7bb9351](https://github.com/GZMOS/PurrNet/commit/7bb93511c5afe551dfa5c73efa29aaad5161120c))
* Whoopsie ([d2ec215](https://github.com/GZMOS/PurrNet/commit/d2ec2152b1da82740e6d7042b197f14f800cd151))
* work around c# methods that generate GC ([59d660f](https://github.com/GZMOS/PurrNet/commit/59d660fcda78f1fe5543350ad28c6035accba61c))
* write read concrete NetworkIdentity type ([37cdd12](https://github.com/GZMOS/PurrNet/commit/37cdd12bbad28bcaa22d0ea71198113538e2828b))
* write/read with modifier bad history ([b8a7e3c](https://github.com/GZMOS/PurrNet/commit/b8a7e3c2ee6fc04f49279fbbed66db7783793749))
* writer for Ray2D ([24587cd](https://github.com/GZMOS/PurrNet/commit/24587cd0ee263e44e04997e3d09626300691f2e4))
* wrong local variable index ([612cbfd](https://github.com/GZMOS/PurrNet/commit/612cbfd0aa2f96f6dec7a8814062bc50e4652bf9))
* wtf, how did i not see this before ([541652c](https://github.com/GZMOS/PurrNet/commit/541652ca3d80861c240db8780c5af3ba7585e3c9))


### Continuous Integration

* **release:** 1.13.0-beta.31 [skip ci] ([b1a396c](https://github.com/GZMOS/PurrNet/commit/b1a396c72e2313680f9adbc7ca46add33be67282))
* **release:** 2.0.0 [skip ci] ([8610eb5](https://github.com/GZMOS/PurrNet/commit/8610eb5cac1470f79c3df57443674b0a9df94271))


### Features

* add `compressionLevel` to RPC attributes ([f67b01b](https://github.com/GZMOS/PurrNet/commit/f67b01b3a2918cab0ebcdce7457d0fd69e660ca2))
* add a new rule 'enable host migration' ([7b9b083](https://github.com/GZMOS/PurrNet/commit/7b9b083a94773370ba2cd004a07e349eb2c4d8cd))
* add CompressedVector2 for 2D vector compression ([57a0213](https://github.com/GZMOS/PurrNet/commit/57a021325b3734f60ba37ed4ab1eee4703594501))
* Add implicit conversion operators for CompressedVector3 <-> Vector2 ([b44f7b5](https://github.com/GZMOS/PurrNet/commit/b44f7b5a2463aa7ea50affd6339244d1196f6885))
* add Packer.HasPacker and DeltaPacker.HasPacker ([a643b7b](https://github.com/GZMOS/PurrNet/commit/a643b7b30895e2f3be34c925d77b2282a456be8d))
* add SaveHasherInFile component to compare hashes between editor and builds ([beae3d6](https://github.com/GZMOS/PurrNet/commit/beae3d6117ba32d90659f6a353036ed3a4e592e3))
* add toolbar display settings ([f289470](https://github.com/GZMOS/PurrNet/commit/f289470cb3f40623bc434c16afd79b4fc9cd98a7))
* added a flush method for hierarchy actions ([f30872d](https://github.com/GZMOS/PurrNet/commit/f30872d21136bcc523bf6e75f0110ba33e2e6bf3))
* added GetSteamID to NetworkManager and Transport ([7f2cff4](https://github.com/GZMOS/PurrNet/commit/7f2cff4f06b6ea819ee929130cc3b7953c17d3cf))
* Added PlayerIdentity ([93ffe55](https://github.com/GZMOS/PurrNet/commit/93ffe558807b310e344348986d8aab4755893633))
* added PurrMonoBehaviour ([98b068a](https://github.com/GZMOS/PurrNet/commit/98b068a7fe50811485d4de8423cba5bb11e9d216))
* Added Validated Syncvar ([49f9343](https://github.com/GZMOS/PurrNet/commit/49f9343b86e10e9f8d1b1db5c5f4c878aa19eb2f))
* Adjustable sync tick update rate ([51d5388](https://github.com/GZMOS/PurrNet/commit/51d5388af45121a7b2b76c7d8fd8c91fd162597a))
* allow custom server for the purrtransport ([e835e63](https://github.com/GZMOS/PurrNet/commit/e835e63580a59d86ca55e7a528ff53cf765d5e6c))
* allow to change scene visibility dynamically ([7c264bc](https://github.com/GZMOS/PurrNet/commit/7c264bcba63a0d192060548f38e19c0ee40411f9))
* allow to enable/disable purr buttons ([7d37c56](https://github.com/GZMOS/PurrNet/commit/7d37c5693171ce82217cdd978a4084c53323effa))
* allow to force ipv4 for web transport ([83756ea](https://github.com/GZMOS/PurrNet/commit/83756eae66255ae2e1abac7e0009690876f1e59b))
* allow to not delta compress certain fields ([f320274](https://github.com/GZMOS/PurrNet/commit/f32027485614946ff34d65e8cfc5f730304fe402))
* allow to set BitPacker position ([3c9fc0a](https://github.com/GZMOS/PurrNet/commit/3c9fc0afb0ad13a408f3321604b5ccb67c2a69d7))
* authenticator! ([2d2c1f1](https://github.com/GZMOS/PurrNet/commit/2d2c1f1257797e55c8f0665fd5da418ac2dc6f01))
* auto math `IMath<T>` interface (mostly meant for prediction) ([c7a9d9f](https://github.com/GZMOS/PurrNet/commit/c7a9d9f0fcc5613e17ea30251fb427dc91cf039f))
* auto stop when losing connection ([2e7d8ab](https://github.com/GZMOS/PurrNet/commit/2e7d8ab8c23e62ce9d57dcc08008b4d0a2d47d27))
* bots ([36b706f](https://github.com/GZMOS/PurrNet/commit/36b706f250f2e2a0b157ac193e5d25b5fcca1616))
* Broadcast subscribe queueing added ([585bc4d](https://github.com/GZMOS/PurrNet/commit/585bc4d854a04df84a3a590554bf8d37274999a4))
* client/server purrnet version missmatch checker ([3387274](https://github.com/GZMOS/PurrNet/commit/3387274f24a8e1e9a33aaf502d3e81afc6d35b4d))
* collider rollback with some tests ([1961455](https://github.com/GZMOS/PurrNet/commit/1961455dde9e4f9695ab6e9509339c6a9cf0d5b2))
* Copy my SteamID to clipboard ([8d504e4](https://github.com/GZMOS/PurrNet/commit/8d504e43a9df6d5c5da622b61457362d7730782a))
* delta packing is aware of changes ([9ca58fa](https://github.com/GZMOS/PurrNet/commit/9ca58fa30eb361d0f28c3f466f701042c28cf757))
* delta serializers for basic C# numeric types (floats, ints) ([93e8ebd](https://github.com/GZMOS/PurrNet/commit/93e8ebd30d93a13e47a7efec60a8db962b4abb55))
* drag drop event in editor ([1d3166e](https://github.com/GZMOS/PurrNet/commit/1d3166e187852029173e953013c30d253b58215e))
* Enable Pool Debug menu item ([c53c455](https://github.com/GZMOS/PurrNet/commit/c53c455b5265b74fd1a46e0975e1b505d7457b10))
* endian checks ([5ebbe9f](https://github.com/GZMOS/PurrNet/commit/5ebbe9f220b9790413f42e72151629ec4788ce40))
* GameObjectPrototype from NetworkIdentity ([9028b41](https://github.com/GZMOS/PurrNet/commit/9028b41a54031e33b51ff644d2ebbb574d4bedc9))
* include preserve attributes on types that are networked ([824065e](https://github.com/GZMOS/PurrNet/commit/824065e75684e0e42e80f29c1136750b7ff331bf))
* introduce `BitPackerWithLength` ([672c623](https://github.com/GZMOS/PurrNet/commit/672c6234fd42e0119a62deae68f08b56f453339e))
* introduce `RawNetManager` ([59aa743](https://github.com/GZMOS/PurrNet/commit/59aa743f1f366431135b0846ceb8c63ddbad4937))
* introduce api to HierarchyV2 module that allows to manually manage spawning and observability events for lower level control ([9825580](https://github.com/GZMOS/PurrNet/commit/982558000c56142ed472b205e38a6a96e4aff96e))
* IStandaloneSerializable ([27e1733](https://github.com/GZMOS/PurrNet/commit/27e17337811855f0b0e5d486416bb1713cffe333))
* MUCH better editor GUI in my opinion :) ([73205e1](https://github.com/GZMOS/PurrNet/commit/73205e187b123e25179d09b4bd49088c95fc1a90))
* Network assets added ([16ebe3c](https://github.com/GZMOS/PurrNet/commit/16ebe3c4e91db8ab14f0d7c075294bae0354f33c))
* network manager & tweaks to the inspector GUI ([17121b2](https://github.com/GZMOS/PurrNet/commit/17121b2fd59451fb92284fb8961ed32b4d3fdbe1))
* Network Manager status added ([9268789](https://github.com/GZMOS/PurrNet/commit/9268789900ed4d67af7d7aefa33e24ebfd6d074e))
* Network prefabs - pooling settings ([e1c41b6](https://github.com/GZMOS/PurrNet/commit/e1c41b6cfa1d08dc37badac293ce80513e72876b))
* Network Rigidbody ([7ca21d4](https://github.com/GZMOS/PurrNet/commit/7ca21d43033c3ecc8b9e154f51782b5da14f5e54))
* Network server toggle added ([05af05b](https://github.com/GZMOS/PurrNet/commit/05af05b37d10a428bbdffc37fdaae40d3527d5f0))
* on network started and shutdown events on the networkManager ([72b65f2](https://github.com/GZMOS/PurrNet/commit/72b65f265c0942f17710f8923fddbc53ae2d19c4))
* onStateChanged now contains data for previous and new state ([0bc8fe0](https://github.com/GZMOS/PurrNet/commit/0bc8fe03344882b1d3d9c9b365165cbece9352a1))
* overriding delete behaviour in scene view and hierarchy ([7ff61c0](https://github.com/GZMOS/PurrNet/commit/7ff61c01e4a5fcbebd33a3ef255a320a39a0f04d))
* ownership! ([45f70cd](https://github.com/GZMOS/PurrNet/commit/45f70cdd64f5da8c75a3b717323b98fc27b1a820))
* parameter serialization ([e44c87e](https://github.com/GZMOS/PurrNet/commit/e44c87e74e93d71a294d7d5cee9205bb33ead317))
* pickle delta data :p ([17db643](https://github.com/GZMOS/PurrNet/commit/17db643bae79a28e78800b24dfee3b676207d78f))
* promoting client to server ([36569e4](https://github.com/GZMOS/PurrNet/commit/36569e494030125dccf757572e2debc54e470ca3))
* Purr button attribute added ([322278d](https://github.com/GZMOS/PurrNet/commit/322278d4d202a141320dc14c86c775808fdcb9a4))
* purrnet relay for dev purposes ([c66699b](https://github.com/GZMOS/PurrNet/commit/c66699ba869262edfc7cdb6b394a245599247949))
* PurrTransport new inspector with region selector ([15aadab](https://github.com/GZMOS/PurrNet/commit/15aadabac61e77756f244ad945420e48f4dd91b8))
* purrtransport udp support ([1a0ad4a](https://github.com/GZMOS/PurrNet/commit/1a0ad4ada2cc5becc5cf09473a9c8212fb8ac1ef))
* remove irrelevant setactives and setenableds ([1b1f72a](https://github.com/GZMOS/PurrNet/commit/1b1f72a10155d04e6e3cf6ccd2be8bad70e2bb91))
* rpc batching and header delta compression ([639532c](https://github.com/GZMOS/PurrNet/commit/639532c4a790e1b8970c67737a79d54c53f69e58))
* rpc buffering (wip) ([87aac57](https://github.com/GZMOS/PurrNet/commit/87aac57187dea668660dbbf368beef007bc7db5d))
* Run context guarded methods ([9309fb6](https://github.com/GZMOS/PurrNet/commit/9309fb64810a9595221e174d0ae21aa93ee93cce))
* scene object!! ([c6dc754](https://github.com/GZMOS/PurrNet/commit/c6dc7549235767d19f3072662af59f62f3963b77))
* sending NetworkModule references over the network ([a183682](https://github.com/GZMOS/PurrNet/commit/a1836824bcf6363c4b91cc3b02d3d6672a9d9a6c))
* serializers for disposable list and hashset ([f8734da](https://github.com/GZMOS/PurrNet/commit/f8734da779eae15d562caccfea1919defab91b02))
* simple 'TakeOwnership' context menu for NetworkIdentity ([5fe79a1](https://github.com/GZMOS/PurrNet/commit/5fe79a1554160a0261a735b6a2dd49eec54a2153))
* spawn validator for client spawning ([569ef7a](https://github.com/GZMOS/PurrNet/commit/569ef7a38a6b136f13d725ac993162d547e51e51))
* start flags instead of 50 booleans ([4161f91](https://github.com/GZMOS/PurrNet/commit/4161f9125ee95b5f3e004c2aafd4a3fd24a1e819))
* State machine conditionals QoL added ([5fe42f7](https://github.com/GZMOS/PurrNet/commit/5fe42f7123cd04a49c3d1b1f47e27ba862de2dda))
* State machine owner auth added ([66913db](https://github.com/GZMOS/PurrNet/commit/66913db08a466af8ba46d057b6f86450c79027eb))
* Sync Array added ([e2409b9](https://github.com/GZMOS/PurrNet/commit/e2409b93645440381255bb400d5631dc6e3615b7))
* Sync Queue added ([84fae0b](https://github.com/GZMOS/PurrNet/commit/84fae0bded7f5402d34d909564718675d2fa57d8))
* Sync Timer pause & resume added ([c12d975](https://github.com/GZMOS/PurrNet/commit/c12d975c7424d004e9b5514657037fa64b2d7cb1))
* Synced tick added ([129ef53](https://github.com/GZMOS/PurrNet/commit/129ef539440c764b6f62857ce2005fe7342e185c))
* unity editor toolbar with purrnet state ([dbdb6cb](https://github.com/GZMOS/PurrNet/commit/dbdb6cb04ac88fb364826430c2a32273ad8e79b8))
* UnityProxy for instantiate and destroy ([7fa4358](https://github.com/GZMOS/PurrNet/commit/7fa43580e9219adfd9d6658f7f199c3b2eb7943c))


### BREAKING CHANGES

* **release:** fixed type in `AuthenticationBehaviour<T>`, renamed `GetClientPlayload` to `GetClientPayload` ([b03e333](https://github.com/PurrNet/PurrNet/commit/b03e333c40c3e637b67806041136c29df4ff3276))
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
* **release:** fixed type in `AuthenticationBehaviour<T>`, renamed `GetClientPlayload` to `GetClientPayload` ([b03e333](https://github.com/PurrNet/PurrNet/commit/b03e333c40c3e637b67806041136c29df4ff3276))

# [1.19.0-beta.6](https://github.com/PurrNet/PurrNet/compare/v1.19.0-beta.5...v1.19.0-beta.6) (2026-01-30)


### Bug Fixes

* Added dynamic force correction to network RB ([c6a27cf](https://github.com/PurrNet/PurrNet/commit/c6a27cfbac65ec379c33c88bc8d939091a2b4ed7))
* Scaling hard correction threshold of Network RB ([39d5cb2](https://github.com/PurrNet/PurrNet/commit/39d5cb21b1ffba2c0db9e2210eabfe40b5fec525))
* Smooth rotational syncing on Network RB ([a4ca898](https://github.com/PurrNet/PurrNet/commit/a4ca898e5372365cf26f3c30ec968c1c6354891c))
* Solve inconsistency in sync timing ([59e7993](https://github.com/PurrNet/PurrNet/commit/59e7993fbdf64b5cee42e32b19593766e6d377bc))

# [1.19.0-beta.5](https://github.com/PurrNet/PurrNet/compare/v1.19.0-beta.4...v1.19.0-beta.5) (2026-01-29)


### Bug Fixes

* GetHashCode fails if list is null ([0ab9281](https://github.com/PurrNet/PurrNet/commit/0ab9281b48dce6cb9124dc7a6a601fbb7e7880eb))

# [1.19.0-beta.4](https://github.com/PurrNet/PurrNet/compare/v1.19.0-beta.3...v1.19.0-beta.4) (2026-01-28)


### Bug Fixes

* handle exceptions during tick processing in ServerTick method ([78f1408](https://github.com/PurrNet/PurrNet/commit/78f14086bede0a9411ec507e4f47edd191ad1ee3))

# [1.19.0-beta.3](https://github.com/PurrNet/PurrNet/compare/v1.19.0-beta.2...v1.19.0-beta.3) (2026-01-28)


### Bug Fixes

* make sure we account for local client being gone when handling ticks ([ca622e5](https://github.com/PurrNet/PurrNet/commit/ca622e51372b387b4f687d577341bfcd5f2223a5))

# [1.19.0-beta.2](https://github.com/PurrNet/PurrNet/compare/v1.19.0-beta.1...v1.19.0-beta.2) (2026-01-28)


### Bug Fixes

* InvokeLocal wasn't reseting position properly all the time ([66c8536](https://github.com/PurrNet/PurrNet/commit/66c8536ecde5bba6010104f7039cfab3e528a48c))

# [1.19.0-beta.1](https://github.com/PurrNet/PurrNet/compare/v1.18.1-beta.36...v1.19.0-beta.1) (2026-01-27)


### Features

* Network Rigidbody ([7ca21d4](https://github.com/PurrNet/PurrNet/commit/7ca21d43033c3ecc8b9e154f51782b5da14f5e54))

## [1.18.1-beta.36](https://github.com/PurrNet/PurrNet/compare/v1.18.1-beta.35...v1.18.1-beta.36) (2026-01-27)


### Bug Fixes

* using after free ([890a29c](https://github.com/PurrNet/PurrNet/commit/890a29cd72bf96a70665da77c37aca34caaaa187))

## [1.18.1-beta.35](https://github.com/PurrNet/PurrNet/compare/v1.18.1-beta.34...v1.18.1-beta.35) (2026-01-25)


### Bug Fixes

* target RPC to host's client failed, this was a regression ([6fdce17](https://github.com/PurrNet/PurrNet/commit/6fdce17673990deb758bd2d4b2348a00ad16c9dd))

## [1.18.1-beta.34](https://github.com/PurrNet/PurrNet/compare/v1.18.1-beta.33...v1.18.1-beta.34) (2026-01-22)


### Bug Fixes

* initialize ping history size and stats on server connection ([5f8689a](https://github.com/PurrNet/PurrNet/commit/5f8689ae9ae785e2764725bb7f53a0c6ed018aa2))

## [1.18.1-beta.33](https://github.com/PurrNet/PurrNet/compare/v1.18.1-beta.32...v1.18.1-beta.33) (2026-01-21)


### Bug Fixes

* DisposableArray duplicate ([af07f84](https://github.com/PurrNet/PurrNet/commit/af07f84b4d71d01a665aa797e71e58502ed11be9))

## [1.18.1-beta.32](https://github.com/PurrNet/PurrNet/compare/v1.18.1-beta.31...v1.18.1-beta.32) (2026-01-21)


### Bug Fixes

* prevent destruction of objects in editor mode and cancel if it was already destroyed ([5351ba5](https://github.com/PurrNet/PurrNet/commit/5351ba52cd4d0569c9c1c05b349f83dd583a6351))

## [1.18.1-beta.31](https://github.com/PurrNet/PurrNet/compare/v1.18.1-beta.30...v1.18.1-beta.31) (2026-01-20)


### Bug Fixes

* packer duplicate mistake; tests for hasher ([b66ca76](https://github.com/PurrNet/PurrNet/commit/b66ca76f528746e4aa126d90948482ec26e481e6))

## [1.18.1-beta.30](https://github.com/PurrNet/PurrNet/compare/v1.18.1-beta.29...v1.18.1-beta.30) (2026-01-20)


### Bug Fixes

* add deterministic hash to the packer ([ceaaf4d](https://github.com/PurrNet/PurrNet/commit/ceaaf4d08db74443a67bdadf533bcd5d955d2c90))

## [1.18.1-beta.29](https://github.com/PurrNet/PurrNet/compare/v1.18.1-beta.28...v1.18.1-beta.29) (2026-01-20)


### Bug Fixes

* hash collisions would break delta compression, added type explicitly to avoid this ([bc670ab](https://github.com/PurrNet/PurrNet/commit/bc670ab69682922c31c05fefae40c16f19f4f02f))

## [1.18.1-beta.28](https://github.com/PurrNet/PurrNet/compare/v1.18.1-beta.27...v1.18.1-beta.28) (2026-01-20)


### Bug Fixes

* GetModule can fail here ([4ea9c5a](https://github.com/PurrNet/PurrNet/commit/4ea9c5ab6133cf14deace076f556649f78619cb6))

## [1.18.1-beta.27](https://github.com/PurrNet/PurrNet/compare/v1.18.1-beta.26...v1.18.1-beta.27) (2026-01-19)


### Bug Fixes

* myers diff GC fixes and some tests for consistency ([6fc9525](https://github.com/PurrNet/PurrNet/commit/6fc95257d3aa4d0c2651b68e39e0a17d2bca8583))

## [1.18.1-beta.26](https://github.com/PurrNet/PurrNet/compare/v1.18.1-beta.25...v1.18.1-beta.26) (2026-01-19)


### Bug Fixes

* dynamically changing region was error prone for the purrtransport ([92e2bb0](https://github.com/PurrNet/PurrNet/commit/92e2bb079091a474b5397327a7efe1e812e62235))

## [1.18.1-beta.25](https://github.com/PurrNet/PurrNet/compare/v1.18.1-beta.24...v1.18.1-beta.25) (2026-01-18)


### Bug Fixes

* Quaternion equality check is stupid ([69b1c32](https://github.com/PurrNet/PurrNet/commit/69b1c32d766429307ce0d2b277c45994c53fd838))

## [1.18.1-beta.24](https://github.com/PurrNet/PurrNet/compare/v1.18.1-beta.23...v1.18.1-beta.24) (2026-01-15)


### Bug Fixes

* remove commented-out code in PlayModePatch ([d1f866c](https://github.com/PurrNet/PurrNet/commit/d1f866c2a0c640764578a12c99b0a6241741492e))

## [1.18.1-beta.23](https://github.com/PurrNet/PurrNet/compare/v1.18.1-beta.22...v1.18.1-beta.23) (2026-01-15)


### Bug Fixes

* improve network assets reliability and synchronization ([c508cd1](https://github.com/PurrNet/PurrNet/commit/c508cd10b733aa9ae841ad7b9a54712766b1a8fe))

## [1.18.1-beta.22](https://github.com/PurrNet/PurrNet/compare/v1.18.1-beta.21...v1.18.1-beta.22) (2026-01-14)


### Bug Fixes

* array comparison ([a278892](https://github.com/PurrNet/PurrNet/commit/a278892d44e3ca79d30a9d5becdc5469f07cff52))

## [1.18.1-beta.21](https://github.com/PurrNet/PurrNet/compare/v1.18.1-beta.20...v1.18.1-beta.21) (2026-01-14)


### Bug Fixes

* pass asServer parameter to TickManager in NetworkManager and RawNetManager ([0c288b3](https://github.com/PurrNet/PurrNet/commit/0c288b332028caf1f937014201f78ec9f5e8d688))

## [1.18.1-beta.20](https://github.com/PurrNet/PurrNet/compare/v1.18.1-beta.19...v1.18.1-beta.20) (2026-01-12)


### Bug Fixes

* add aggressive inlining to Duplicate method for JIT performance improvement ([f46516e](https://github.com/PurrNet/PurrNet/commit/f46516e8aafcd64759874fd3546148bb5cc06ce4))

## [1.18.1-beta.19](https://github.com/PurrNet/PurrNet/compare/v1.18.1-beta.18...v1.18.1-beta.19) (2026-01-12)


### Bug Fixes

* handle null selfRef and improve error handling in IEquatable generation ([44166b5](https://github.com/PurrNet/PurrNet/commit/44166b5a6d826afadcbb85c7d7f3af80586c1e54))

## [1.18.1-beta.18](https://github.com/PurrNet/PurrNet/compare/v1.18.1-beta.17...v1.18.1-beta.18) (2026-01-10)


### Bug Fixes

* delta module proper MTU usage instead of hard coded value ([6e9a1ef](https://github.com/PurrNet/PurrNet/commit/6e9a1efd41878cc010930a1ca1ed4be5e2118c6f))

## [1.18.1-beta.17](https://github.com/PurrNet/PurrNet/compare/v1.18.1-beta.16...v1.18.1-beta.17) (2026-01-09)


### Bug Fixes

* dont include rpc related things here ([f986586](https://github.com/PurrNet/PurrNet/commit/f98658604cdcc4266b52f090ce97c57e50d0ff12))

## [1.18.1-beta.16](https://github.com/PurrNet/PurrNet/compare/v1.18.1-beta.15...v1.18.1-beta.16) (2026-01-09)


### Bug Fixes

* if transform from A to B but types mismatch create it from scratch ([2e1799c](https://github.com/PurrNet/PurrNet/commit/2e1799c7bb5a3e4f78459093207f60601db21fa1))

## [1.18.1-beta.15](https://github.com/PurrNet/PurrNet/compare/v1.18.1-beta.14...v1.18.1-beta.15) (2026-01-09)


### Bug Fixes

* still cull rpcs when player isnt observer for other channels ([69c2677](https://github.com/PurrNet/PurrNet/commit/69c26771081843302480415c118fe556351b8cf7))

## [1.18.1-beta.14](https://github.com/PurrNet/PurrNet/compare/v1.18.1-beta.13...v1.18.1-beta.14) (2026-01-09)


### Bug Fixes

* Improved GC of statistics manager ([1d563a8](https://github.com/PurrNet/PurrNet/commit/1d563a8234393851ac2bdfdac9df6a80250a821a))

## [1.18.1-beta.13](https://github.com/PurrNet/PurrNet/compare/v1.18.1-beta.12...v1.18.1-beta.13) (2026-01-08)


### Bug Fixes

* Host migration should be enabled to check if it must force CanSee or not ([5814337](https://github.com/PurrNet/PurrNet/commit/58143374834ae94d908a8d552a6150d45bd45c00))

## [1.18.1-beta.12](https://github.com/PurrNet/PurrNet/compare/v1.18.1-beta.11...v1.18.1-beta.12) (2026-01-07)


### Bug Fixes

* properly call despawn() even if not fully spawned ([0b1739f](https://github.com/PurrNet/PurrNet/commit/0b1739feb6aba7e3eccef27d410898c4a435aeaf))

## [1.18.1-beta.11](https://github.com/PurrNet/PurrNet/compare/v1.18.1-beta.10...v1.18.1-beta.11) (2026-01-07)


### Bug Fixes

* Host migration should be enabled to check if it must force scene public or not. ([576e263](https://github.com/PurrNet/PurrNet/commit/576e263412f790af8c161fe1993d2fddbac39ebc))

## [1.18.1-beta.10](https://github.com/PurrNet/PurrNet/compare/v1.18.1-beta.9...v1.18.1-beta.10) (2026-01-03)


### Bug Fixes

* Whoopsie ([d2ec215](https://github.com/PurrNet/PurrNet/commit/d2ec2152b1da82740e6d7042b197f14f800cd151))

## [1.18.1-beta.9](https://github.com/PurrNet/PurrNet/compare/v1.18.1-beta.8...v1.18.1-beta.9) (2026-01-02)


### Bug Fixes

* expose latest read data properties for network transform ([65e6a23](https://github.com/PurrNet/PurrNet/commit/65e6a236b5819fec9e74002c886b245014713bbe))

## [1.18.1-beta.8](https://github.com/PurrNet/PurrNet/compare/v1.18.1-beta.7...v1.18.1-beta.8) (2026-01-02)


### Bug Fixes

* Sync dictionary serialization upgrade ([5934acb](https://github.com/PurrNet/PurrNet/commit/5934acbf9a52511353941040a1643728d658a75f))

## [1.18.1-beta.7](https://github.com/PurrNet/PurrNet/compare/v1.18.1-beta.6...v1.18.1-beta.7) (2026-01-02)


### Bug Fixes

* Added state machine current state helpers ([6ebfff9](https://github.com/PurrNet/PurrNet/commit/6ebfff9a934aadbf3e0c3010fff2632751e3538d))

## [1.18.1-beta.6](https://github.com/PurrNet/PurrNet/compare/v1.18.1-beta.5...v1.18.1-beta.6) (2025-12-31)


### Bug Fixes

* cleanup server/client modules, this caused some issues when next playthrough starting mode was changed ([65b2d0c](https://github.com/PurrNet/PurrNet/commit/65b2d0c6a330514edfb40d28c37cb75716b9a00d))
* syncvar invalidate is controller earlier ([2444436](https://github.com/PurrNet/PurrNet/commit/2444436aded533525be1e33930dab28a367e8cc2))
* wrong local variable index ([612cbfd](https://github.com/PurrNet/PurrNet/commit/612cbfd0aa2f96f6dec7a8814062bc50e4652bf9))

## [1.18.1-beta.5](https://github.com/PurrNet/PurrNet/compare/v1.18.1-beta.4...v1.18.1-beta.5) (2025-12-26)


### Bug Fixes

* ActualGetRelayServersAsync sometimes would come with an empty string and throw and exception ([c186213](https://github.com/PurrNet/PurrNet/commit/c1862139fb17591e678acfc40a9ded9886b5ee83))

## [1.18.1-beta.4](https://github.com/PurrNet/PurrNet/compare/v1.18.1-beta.3...v1.18.1-beta.4) (2025-12-22)


### Bug Fixes

* instead of throwing an exception lets just log what happened ([4a1c0b8](https://github.com/PurrNet/PurrNet/commit/4a1c0b8047d9ca08016a6f028a35cdf639a16483))
* some safety when cleaning client state ([f7011af](https://github.com/PurrNet/PurrNet/commit/f7011afaba24e90fdf80eb90a0509a274c68d1f4))

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
