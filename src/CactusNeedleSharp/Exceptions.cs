namespace CactusNeedleSharp;

public class NeedleException : Exception { public NeedleException(string message, Exception? inner = null) : base(message, inner) { } }
public sealed class NeedleInitializationException : NeedleException { public NeedleInitializationException(string message, Exception? inner = null) : base(message, inner) { } }
public sealed class NeedleNativeLibraryException : NeedleException { public NeedleNativeLibraryException(string message, Exception? inner = null) : base(message, inner) { } }
public class NeedleArtifactException : NeedleException { public NeedleArtifactException(string message, Exception? inner = null) : base(message, inner) { } }
public sealed class NeedleArtifactNotFoundException : NeedleArtifactException { public NeedleArtifactNotFoundException(string message) : base(message) { } }
public sealed class NeedleSchemaException : NeedleException { public NeedleSchemaException(string message, Exception? inner = null) : base(message, inner) { } }
public sealed class NeedleInferenceException : NeedleException { public NeedleInferenceException(string message, Exception? inner = null) : base(message, inner) { } }
public sealed class NeedleProtocolException : NeedleException { public NeedleProtocolException(string message, Exception? inner = null) : base(message, inner) { } }
