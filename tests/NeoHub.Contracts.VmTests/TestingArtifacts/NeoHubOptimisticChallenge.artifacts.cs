using Neo.Cryptography.ECC;
using Neo.Extensions;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Numerics;

#pragma warning disable CS0067

namespace Neo.SmartContract.Testing;

public abstract class NeoHubOptimisticChallenge(Neo.SmartContract.Testing.SmartContractInitialize initialize) : Neo.SmartContract.Testing.SmartContract(initialize), IContractInfo
{
    #region Compiled data

    public static Neo.SmartContract.Manifest.ContractManifest Manifest => Neo.SmartContract.Manifest.ContractManifest.Parse(@"{""name"":""NeoHub.OptimisticChallenge"",""groups"":[],""features"":{},""supportedstandards"":[],""abi"":{""methods"":[{""name"":""_deploy"",""parameters"":[{""name"":""data"",""type"":""Any""},{""name"":""update"",""type"":""Boolean""}],""returntype"":""Void"",""offset"":0,""safe"":false},{""name"":""getOwner"",""parameters"":[],""returntype"":""Hash160"",""offset"":282,""safe"":true},{""name"":""setOwner"",""parameters"":[{""name"":""newOwner"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":381,""safe"":false},{""name"":""getWindowSeconds"",""parameters"":[],""returntype"":""Integer"",""offset"":502,""safe"":true},{""name"":""getChallengerRewardBps"",""parameters"":[],""returntype"":""Integer"",""offset"":566,""safe"":true},{""name"":""setWindowSeconds"",""parameters"":[{""name"":""seconds"",""type"":""Integer""}],""returntype"":""Void"",""offset"":639,""safe"":false},{""name"":""setChallengerRewardBps"",""parameters"":[{""name"":""bps"",""type"":""Integer""}],""returntype"":""Void"",""offset"":771,""safe"":false},{""name"":""registerFraudVerifier"",""parameters"":[{""name"":""verifier"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":897,""safe"":false},{""name"":""registerPermissionlessFraudVerifier"",""parameters"":[{""name"":""verifier"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":1181,""safe"":false},{""name"":""revokeFraudVerifier"",""parameters"":[{""name"":""verifier"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":1500,""safe"":false},{""name"":""isApprovedFraudVerifier"",""parameters"":[{""name"":""verifier"",""type"":""Hash160""}],""returntype"":""Boolean"",""offset"":1644,""safe"":true},{""name"":""isPermissionlessFraudVerifier"",""parameters"":[{""name"":""verifier"",""type"":""Hash160""}],""returntype"":""Boolean"",""offset"":1691,""safe"":true},{""name"":""openWindow"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""batchNumber"",""type"":""Integer""},{""name"":""sequencer"",""type"":""Hash160""}],""returntype"":""Integer"",""offset"":1738,""safe"":false},{""name"":""challenge"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""batchNumber"",""type"":""Integer""},{""name"":""challenger"",""type"":""Hash160""},{""name"":""fraudProofBytes"",""type"":""ByteArray""},{""name"":""fraudVerifier"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":2499,""safe"":false},{""name"":""finalizeIfPastWindow"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""batchNumber"",""type"":""Integer""}],""returntype"":""Void"",""offset"":3463,""safe"":false},{""name"":""isWindowOpen"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""batchNumber"",""type"":""Integer""},{""name"":""nowUnixSeconds"",""type"":""Integer""}],""returntype"":""Boolean"",""offset"":3731,""safe"":true},{""name"":""getDeadline"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""batchNumber"",""type"":""Integer""}],""returntype"":""Integer"",""offset"":3770,""safe"":true}],""events"":[{""name"":""WindowOpened"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Integer""},{""name"":""arg3"",""type"":""Integer""},{""name"":""arg4"",""type"":""Hash160""}]},{""name"":""ChallengeAccepted"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Integer""},{""name"":""arg3"",""type"":""Hash160""},{""name"":""arg4"",""type"":""Integer""}]},{""name"":""WindowFinalized"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Integer""}]},{""name"":""FraudVerifierApproved"",""parameters"":[{""name"":""obj"",""type"":""Hash160""}]},{""name"":""PermissionlessVerifierApproved"",""parameters"":[{""name"":""obj"",""type"":""Hash160""}]},{""name"":""FraudVerifierRevoked"",""parameters"":[{""name"":""obj"",""type"":""Hash160""}]},{""name"":""WindowSecondsChanged"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Integer""}]},{""name"":""ChallengerRewardBpsChanged"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Integer""}]},{""name"":""OwnerChanged"",""parameters"":[{""name"":""arg1"",""type"":""Hash160""},{""name"":""arg2"",""type"":""Hash160""}]}]},""permissions"":[{""contract"":""*"",""methods"":""*""}],""trusts"":[],""extra"":{""Author"":""Neo Project"",""Description"":""Phase-3 optimistic-rollup challenge window for Neo Elastic Network."",""Version"":""0.1.0"",""Sourcecode"":""https://github.com/r3e-network/neo-n4/tree/master/contracts/NeoHub.OptimisticChallenge"",""nef"":{""optimization"":""Basic""}}}");

    /// <summary>
    /// Optimization: "Basic"
    /// </summary>
    public static Neo.SmartContract.NefFile Nef => Convert.FromBase64String(@"TkVGM05lby5Db21waWxlci5DU2hhcnAgMy45LjErYzg3NjZlYTg0OTI5YTA3ZWU3ZmIyOTkxYmM3ODgyMzgzYzkuLi4AAAAAAP3dDlcEAnkmByPQAAAAeHBoEM5xaBHOcmgSznNpStkoJAZFCSIGygAUsyQFCSIGaRCzqiQSDA1pbnZhbGlkIG93bmVy4GpK2SgkBkUJIgbKABSzJAUJIgZqELOqJB8MGmludmFsaWQgc2V0dGxlbWVudCBtYW5hZ2Vy4GtK2SgkBkUJIgbKABSzJAUJIgZrELOqJBsMFmludmFsaWQgc2VxdWVuY2VyIGJvbmTgaQwB/9swNDhqDAH82zA0MGsMAf3bMDQoARAODAEE2zA0OgGIEwwBBdswNDBAStkoJAZFCSIGygAUs0AQs0BXAAJ5eEGb9mfOQeY/GIRAQeY/GIRAQZv2Z85AVwACeXhBm/ZnzkHmPxiEQEHmPxiEQFcBAAwB/9swNC9waAuXJhoMFAAAAAAAAAAAAAAAAAAAAAAAAAAAIg5oStgkCUrKABQoAzoiAkBXAAF4QZv2Z85Bkl3oMUBBkl3oMUAMFAAAAAAAAAAAAAAAAAAAAAAAAAAAQFcBATSaQfgn7IwkEwwObm90IGF1dGhvcml6ZWTgeErZKCQGRQkiBsoAFLMkBQkiBngQs6okFgwRaW52YWxpZCBuZXcgb3duZXLgNVP///9weAwB/9swNRX///94aBLADAxPd25lckNoYW5nZWRBlQFvYUBB+CfsjEBXAQAMAQTbMDVT////cGgLlyYHARAOIhxoStgmBkUQIgTbIUoQAwAAAAABAAAAuyQDOiICQErYJgZFECIE2yFAVwEADAEF2zA1E////3BoC5cmBwGIEyIwaErYJgZFECIE2yFKEAMAAAAAAQAAALskAzpKEC4EIgpKAv//AAAyCAL//wAAkSICQFcBATWY/v//Qfgn7IwkEwwObm90IGF1dGhvcml6ZWTgeAA8uCQFCSIJeAKAOgkAtiQjDB53aW5kb3cgb3V0IG9mIGJvdW5kcyBbNjBzLCA3ZF3gNST///9weAwBBNswNSb+//94aBLADBRXaW5kb3dTZWNvbmRzQ2hhbmdlZEGVAW9hQFcBATUU/v//Qfgn7IwkEwwObm90IGF1dGhvcml6ZWTgeBC3JAUJIgd4ARAntiQaDBVicHMgb3V0IG9mICgwLCAxMDAwMF3gNez+//9weAwBBdswNa79//94aBLADBpDaGFsbGVuZ2VyUmV3YXJkQnBzQ2hhbmdlZEGVAW9hQFcAATWW/f//Qfgn7IwkEwwObm90IGF1dGhvcml6ZWTgeErZKCQGRQkiBsoAFLMkBQkiBngQs6okFQwQaW52YWxpZCB2ZXJpZmllcuB4NEAMAQHbMFA0IngRwAwVRnJhdWRWZXJpZmllckFwcHJvdmVkQZUBb2FAVwACeXhBm/ZnzkHmPxiEQEHmPxiEQFcDAQAViHAWSmgQUdBFeNswcRByIm5pas5KaBFqnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9R0EVqSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfckVqABS1JJBoIgJA2zBAVwABNXr8//9B+CfsjCQTDA5ub3QgYXV0aG9yaXplZOB4StkoJAZFCSIGygAUsyQFCSIGeBCzqiQVDBBpbnZhbGlkIHZlcmlmaWVy4Hg1JP///wwBAdswUDUD////eDRVDAEB2zBQNfX+//94EcAMFUZyYXVkVmVyaWZpZXJBcHByb3ZlZEGVAW9heBHADB5QZXJtaXNzaW9ubGVzc1ZlcmlmaWVyQXBwcm92ZWRBlQFvYUBXAwEAFYhwF0poEFHQRXjbMHEQciJuaWrOSmgRap5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfUdBFakqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3JFagAUtSSQaCICQFcAATU7+///Qfgn7IwkEwwObm90IGF1dGhvcml6ZWTgeErZKCQGRQkiBsoAFLMkBQkiBngQs6okFQwQaW52YWxpZCB2ZXJpZmllcuB4NeX9//80KXg1H////zQheBHADBRGcmF1ZFZlcmlmaWVyUmV2b2tlZEGVAW9hQFcAAXhBm/ZnzkEvWMXtQEEvWMXtQFcAAXhK2SgkBkUJIgbKABSzqiYFCCIFeBCzJgUJIhF4NYL9//81wPr//wuYIgJAVwABeErZKCQGRQkiBsoAFLOqJgUIIgV4ELMmBQkiEXg1lf7//zWR+v//C5giAkBXAwMMAfzbMDV/+v//StgmDkUMCHNtIHVuc2V0OkrYJAlKygAUKAM6cGhB+CfsjCQbDBZub3Qgc2V0dGxlbWVudCBtYW5hZ2Vy4HgQtyQhDBxjaGFpbklkIDAgaXMgcmVzZXJ2ZWQgZm9yIEwx4HpK2SgkBkUJIgbKABSzJAUJIgZ6ELOqJBYMEWludmFsaWQgc2VxdWVuY2Vy4EG3w4gDAegDoUoQLgQiDkoD/////wAAAAAyDAP/////AAAAAJE1bPr//55KEC4EIg5KA/////8AAAAAMgwD/////wAAAACRcXl4NGFyajWe+f//C5ckGAwTd2luZG93IGFscmVhZHkgb3BlbuBqaTWEAQAAUDUe/P//eXg11gEAAHpQNQD5//96aXl4FMAMDFdpbmRvd09wZW5lZEGVAW9haSICQEG3w4gDQFcAAnl4ETQDQFcBAx2IcHhKaBBR0EV5ShAuBCIISgH/ADIGAf8AkUpoEVHQRXkYqUoQLgQiCEoB/wAyBgH/AJFKaBJR0EV5IKlKEC4EIghKAf8AMgYB/wCRSmgTUdBFeQAYqUoQLgQiCEoB/wAyBgH/AJFKaBRR0EV6ShAuBCIISgH/ADIGAf8AkUpoFVHQRXoYqUoQLgQiCEoB/wAyBgH/AJFKaBZR0EV6IKlKEC4EIghKAf8AMgYB/wCRSmgXUdBFegAYqUoQLgQiCEoB/wAyBgH/AJFKaBhR0EV6ACCpShAuBCIISgH/ADIGAf8AkUpoGVHQRXoAKKlKEC4EIghKAf8AMgYB/wCRSmgaUdBFegAwqUoQLgQiCEoB/wAyBgH/AJFKaBtR0EV6ADipShAuBCIISgH/ADIGAf8AkUpoHFHQRWgiAkBXAAEUiEoQeEoQLgQiCEoB/wAyBgH/AJHQShF4GKlKEC4EIghKAf8AMgYB/wCR0EoSeCCpShAuBCIISgH/ADIGAf8AkdBKE3gAGKlKEC4EIghKAf8AMgYB/wCR0CICQFcAAnl4EzVc/v//QFcMBXpB+CfsjCQeDBlubyB3aXRuZXNzIGZvciBjaGFsbGVuZ2Vy4HvKELckFgwRZW1wdHkgZnJhdWQgcHJvb2bgekrZKCQGRQkiBsoAFLMkBQkiBnoQs6okFwwSaW52YWxpZCBjaGFsbGVuZ2Vy4HxK2SgkBkUJIgbKABSzJAUJIgZ8ELOqJBsMFmludmFsaWQgZnJhdWQgdmVyaWZpZXLgfDUH/P//JCAMG2ZyYXVkIHZlcmlmaWVyIG5vdCBhcHByb3ZlZOB8NRD8//8mBQgiDDWF9v//Qfgn7IwkNQwwZnJhdWQgdmVyaWZpZXIgcmVxdWlyZXMgb3duZXIvZ292ZXJuYW5jZSBjby1zaWdu4Hl4NTr9//9waDV09v//cWkLmCQTDA5ubyBvcGVuIHdpbmRvd+Bp2zA1BgIAAHJBt8OIAwHoA6FKEC4EIg5KA/////8AAAAAMgwD/////wAAAACRarYkHAwXY2hhbGxlbmdlIHdpbmRvdyBjbG9zZWTgeXg1LgIAADUF9v//C5ckFQwQYWxyZWFkeSBhY2NlcHRlZOB7eXgTwB0MC3ZlcmlmeUZyYXVkfEFifVtSc2skGQwUZnJhdWQgcHJvb2YgcmVqZWN0ZWTgeXg1Gf7//zWu9f//dGwLmCQaDBVubyByZWNvcmRlZCBzZXF1ZW5jZXLgbErYJAlKygAUKAM6dQwB/dswNXn1//9K2CYQRQwKYm9uZCB1bnNldDpK2CQJSsoAFCgDOnZteBLAFQwKZ2V0QmFsYW5jZW5BYn1bUncHbwcQtyQVDBBubyBib25kIHRvIHNsYXNo4DUJ9v//dwhvB28IoAEQJ6F3CXl4NTQBAAB6UDWg9P//bwkQtyYYem8JbXgUwB8MBXNsYXNobkFifVtSRW8HbwmfdwpvChC3Ji0MFAAAAAAAAAAAAAAAAAAAAAAAAAAAbwpteBTAHwwFc2xhc2huQWJ9W1JFDAH82zA1q/T//0rYJg5FDAhzbSB1bnNldDpK2CQJSsoAFCgDOncLeXgSwB8MC3JldmVydEJhdGNobwtBYn1bUkVvB3p5eBTADBFDaGFsbGVuZ2VBY2NlcHRlZEGVAW9hQFcAAXgQzngRzhioShAuBCIOSgP/////AAAAADIMA/////8AAAAAkZJ4Es4gqEoQLgQiDkoD/////wAAAAAyDAP/////AAAAAJGSeBPOABioShAuBCIOSgP/////AAAAADIMA/////8AAAAAkZIiAkDbMEBXAAJ5eBI1nvr//0BBYn1bUkBXBAJ5eDWE+v//cGg1vvP//3FpC5gkEwwObm8gb3BlbiB3aW5kb3fgadswNVD///9yQbfDiAMB6AOhShAuBCIOSgP/////AAAAADIMA/////8AAAAAkWq3JCAMG2NoYWxsZW5nZSB3aW5kb3cgc3RpbGwgb3BlbuB5eDV0////NUvz//8LlyQqDCViYXRjaCB3YXMgY2hhbGxlbmdlZDsgY2Fubm90IGZpbmFsaXpl4AwB/NswNRXz//9K2CYORQwIc20gdW5zZXQ6StgkCUrKABQoAzpzeXgSwB8MDWZpbmFsaXplQmF0Y2hrQWJ9W1JFeXgSwAwPV2luZG93RmluYWxpemVkQZUBb2FAVwIDeXg1ePn//zW08v//cGgLlyYFCSIQaNswNVT+//9xemm2IgJAVwECeXg1Ufn//zWN8v//cGgLlyYFECIKaNswNS3+//8iAkBw86A7").AsSerializable<Neo.SmartContract.NefFile>();

    #endregion

    #region Events

    public delegate void delChallengeAccepted(BigInteger? arg1, BigInteger? arg2, UInt160? arg3, BigInteger? arg4);

    [DisplayName("ChallengeAccepted")]
    public event delChallengeAccepted? OnChallengeAccepted;

    public delegate void delChallengerRewardBpsChanged(BigInteger? arg1, BigInteger? arg2);

    [DisplayName("ChallengerRewardBpsChanged")]
    public event delChallengerRewardBpsChanged? OnChallengerRewardBpsChanged;

    public delegate void delFraudVerifierApproved(UInt160? obj);

    [DisplayName("FraudVerifierApproved")]
    public event delFraudVerifierApproved? OnFraudVerifierApproved;

    public delegate void delFraudVerifierRevoked(UInt160? obj);

    [DisplayName("FraudVerifierRevoked")]
    public event delFraudVerifierRevoked? OnFraudVerifierRevoked;

    public delegate void delOwnerChanged(UInt160? arg1, UInt160? arg2);

    [DisplayName("OwnerChanged")]
    public event delOwnerChanged? OnOwnerChanged;

    public delegate void delPermissionlessVerifierApproved(UInt160? obj);

    [DisplayName("PermissionlessVerifierApproved")]
    public event delPermissionlessVerifierApproved? OnPermissionlessVerifierApproved;

    public delegate void delWindowFinalized(BigInteger? arg1, BigInteger? arg2);

    [DisplayName("WindowFinalized")]
    public event delWindowFinalized? OnWindowFinalized;

    public delegate void delWindowOpened(BigInteger? arg1, BigInteger? arg2, BigInteger? arg3, UInt160? arg4);

    [DisplayName("WindowOpened")]
    public event delWindowOpened? OnWindowOpened;

    public delegate void delWindowSecondsChanged(BigInteger? arg1, BigInteger? arg2);

    [DisplayName("WindowSecondsChanged")]
    public event delWindowSecondsChanged? OnWindowSecondsChanged;

    #endregion

    #region Properties

    /// <summary>
    /// Safe property
    /// </summary>
    public abstract BigInteger? ChallengerRewardBps { [DisplayName("getChallengerRewardBps")] get; [DisplayName("setChallengerRewardBps")] set; }

    /// <summary>
    /// Safe property
    /// </summary>
    public abstract UInt160? Owner { [DisplayName("getOwner")] get; [DisplayName("setOwner")] set; }

    /// <summary>
    /// Safe property
    /// </summary>
    public abstract BigInteger? WindowSeconds { [DisplayName("getWindowSeconds")] get; [DisplayName("setWindowSeconds")] set; }

    #endregion

    #region Safe methods

    /// <summary>
    /// Safe method
    /// </summary>
    [DisplayName("getDeadline")]
    public abstract BigInteger? GetDeadline(BigInteger? chainId, BigInteger? batchNumber);

    /// <summary>
    /// Safe method
    /// </summary>
    [DisplayName("isApprovedFraudVerifier")]
    public abstract bool? IsApprovedFraudVerifier(UInt160? verifier);

    /// <summary>
    /// Safe method
    /// </summary>
    [DisplayName("isPermissionlessFraudVerifier")]
    public abstract bool? IsPermissionlessFraudVerifier(UInt160? verifier);

    /// <summary>
    /// Safe method
    /// </summary>
    [DisplayName("isWindowOpen")]
    public abstract bool? IsWindowOpen(BigInteger? chainId, BigInteger? batchNumber, BigInteger? nowUnixSeconds);

    #endregion

    #region Unsafe methods

    /// <summary>
    /// Unsafe method
    /// </summary>
    [DisplayName("challenge")]
    public abstract void Challenge(BigInteger? chainId, BigInteger? batchNumber, UInt160? challenger, byte[]? fraudProofBytes, UInt160? fraudVerifier);

    /// <summary>
    /// Unsafe method
    /// </summary>
    [DisplayName("finalizeIfPastWindow")]
    public abstract void FinalizeIfPastWindow(BigInteger? chainId, BigInteger? batchNumber);

    /// <summary>
    /// Unsafe method
    /// </summary>
    [DisplayName("openWindow")]
    public abstract BigInteger? OpenWindow(BigInteger? chainId, BigInteger? batchNumber, UInt160? sequencer);

    /// <summary>
    /// Unsafe method
    /// </summary>
    [DisplayName("registerFraudVerifier")]
    public abstract void RegisterFraudVerifier(UInt160? verifier);

    /// <summary>
    /// Unsafe method
    /// </summary>
    [DisplayName("registerPermissionlessFraudVerifier")]
    public abstract void RegisterPermissionlessFraudVerifier(UInt160? verifier);

    /// <summary>
    /// Unsafe method
    /// </summary>
    [DisplayName("revokeFraudVerifier")]
    public abstract void RevokeFraudVerifier(UInt160? verifier);

    #endregion
}
