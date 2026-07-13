namespace NeoHub.Sp1Groth16Verifier.UnitTests;

internal static class Sp1Groth16PositiveVector
{
    internal const string ProofSha256 = "f0a8f0aec36d156f9458605854f85667e6ad42c9d4550cff22b48f4a5ebf6b10";
    internal const string ProgramVKeySha256 = "7d13471e12ab0adeb46bfcfe24d34eb6fbd25211a1cc0764f3b7eb85e6b53efe";
    internal const string PublicInputHashSha256 = "01c28518437fed3f43a7d512bab3b199f4e36bb2fda0e0d4e93d70f0bab1c0f3";

    internal static byte[] Proof => Convert.FromBase64String(
        "Q4iiHAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAC+FDumYl01swA5QzQgUsJjAW/reRm0oVzJA0FfyU1IAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA2uZkMIQKYwT8gstSKcQY2IPVkZWmdaM/n23BPfKQn2IMGUewMabFaR76EmgQkR7kfvUxE2uOCE3f7zozjnni4WbZamoWGUJ389+PBR/sndpUb0Kauze0UG6Cn0z09QIy00CfFeWwgfqvdguemMkhCKvGLj5PbvJ0aPfav9AIFgAwqqekQpMi6HMB+s1LSQZuk97Tr9n0agWCrDH5kxdjgsv7C8Yz2jhO4XV0R0LUeOWYaa14aBGDt3Ucqu1EjmUiM6Cguu62mWIv7qc6XxrbfoFWNkerZnRc3efRpWunDIL12X43ecRI3wbJ1IqPwFXZVaLZkIsjDhsXvgSq1D9VM=");

    internal static byte[] ProgramVKey => Convert.FromBase64String(
        "ALp8OEZIwI2KXA1RBAnzbSGLoWi/Y5C8tpD9bjOMdYc=");

    internal static byte[] PublicInputHash => Convert.FromBase64String(
        "EPvB+RKuEpa8icigosbLhldVWm8l7ywaoaG9FwVUn2g=");
}
