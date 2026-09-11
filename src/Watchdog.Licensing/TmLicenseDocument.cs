using System.Security.Cryptography;
using System.Text.Json;
namespace Watchdog.Licensing;
public sealed record TmLicensePayload(int FormatVersion,string Product,Guid LicenseId,string CustomerName,string InstallationId,string Edition,int MaximumTemperatureSensors,DateTimeOffset IssuedAtUtc,DateTimeOffset? ExpiresAtUtc);
public sealed record TmLicenseDocument(string Payload,string Signature) {
 static readonly JsonSerializerOptions Options=new(){PropertyNamingPolicy=JsonNamingPolicy.CamelCase,PropertyNameCaseInsensitive=true};
 static byte[] Decode(string v){v=v.Replace('-','+').Replace('_','/');return Convert.FromBase64String(v+new string('=',(4-v.Length%4)%4));}
 static string Encode(byte[] v)=>Convert.ToBase64String(v).TrimEnd('=').Replace('+','-').Replace('/','_');
#if SUPPLIER
 public static TmLicenseDocument Create(TmLicensePayload p,string privateKeyPem){var b=JsonSerializer.SerializeToUtf8Bytes(p,Options);using var key=ECDsa.Create();key.ImportFromPem(privateKeyPem);return new(Encode(b),Encode(key.SignData(b,HashAlgorithmName.SHA256,DSASignatureFormat.Rfc3279DerSequence)));}
#endif
 public static TmLicenseDocument Parse(string json)=>JsonSerializer.Deserialize<TmLicenseDocument>(json,Options)??throw new InvalidDataException("Invalid license file.");
 public string ToJson()=>JsonSerializer.Serialize(this,Options);
 public bool TryVerify(string publicKeyPem,out TmLicensePayload? p,out string error){p=null;error="Invalid TM license signature, product or format.";try{using var key=ECDsa.Create();key.ImportFromPem(publicKeyPem);var b=Decode(Payload);if(!key.VerifyData(b,Decode(Signature),HashAlgorithmName.SHA256,DSASignatureFormat.Rfc3279DerSequence))return false;var candidate=JsonSerializer.Deserialize<TmLicensePayload>(b,Options);if(candidate?.FormatVersion!=2||candidate.Product!="WatchdogTM")return false;p=candidate;error="";return true;}catch{return false;}}
}

