namespace Lumoin.Veritas.Replication;

/// <summary>
/// Supplies the local seams one metadata serve dispatches to: the host's recorder, its committed-record
/// reader, its inbound record apply, and the identity that host answers a version probe under, handed out
/// together so a serve cannot reach one host's recorder while applying to another's — or answer a probe as a
/// host other than the one it read. It is asked once per connection rather than once per endpoint, so a host
/// that starts its runner after its listener answers from the runner it has when the connection arrives.
/// </summary>
/// <returns>The binding one serve dispatches through.</returns>
public delegate MetadataServeBinding ProvideMetadataServeBindingDelegate();
