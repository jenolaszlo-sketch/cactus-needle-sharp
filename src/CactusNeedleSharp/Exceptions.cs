namespace CactusNeedleSharp;

/// <summary>Base exception for all Needle failures.</summary>
public class NeedleException : Exception { public NeedleException(string message, Exception? inner = null) : base(message, inner) { } }

/// <summary>Thrown when the runtime or a session cannot be initialized.</summary>
public sealed class NeedleInitializationException : NeedleException { public NeedleInitializationException(string message, Exception? inner = null) : base(message, inner) { } }

/// <summary>Thrown when the native library cannot be loaded or invoked.</summary>
public sealed class NeedleNativeLibraryException : NeedleException { public NeedleNativeLibraryException(string message, Exception? inner = null) : base(message, inner) { } }

/// <summary>Thrown when runtime artifact download, caching, or verification fails.</summary>
public class NeedleArtifactException : NeedleException { public NeedleArtifactException(string message, Exception? inner = null) : base(message, inner) { } }

/// <summary>Thrown when a required artifact or weights file cannot be found, including offline cache misses.</summary>
public sealed class NeedleArtifactNotFoundException : NeedleArtifactException { public NeedleArtifactNotFoundException(string message) : base(message) { } }

/// <summary>Thrown when a tool definition or schema is invalid.</summary>
public sealed class NeedleSchemaException : NeedleException { public NeedleSchemaException(string message, Exception? inner = null) : base(message, inner) { } }

/// <summary>Thrown when native inference fails.</summary>
public sealed class NeedleInferenceException : NeedleException { public NeedleInferenceException(string message, Exception? inner = null) : base(message, inner) { } }

/// <summary>Thrown when native output cannot be parsed or tool arguments cannot be deserialized.</summary>
public sealed class NeedleProtocolException : NeedleException { public NeedleProtocolException(string message, Exception? inner = null) : base(message, inner) { } }
