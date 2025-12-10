using System;

namespace LoL_Announcer_Editor
{
    public class ArgumentError : Exception
    {
        public ArgumentError(string message) : base($"Argument error: {message}") { }
    }

    public class FileOpenError : Exception
    {
        public FileOpenError(string filename) : base($"Error opening: {filename}") { }
    }

    public class ParseError : Exception
    {
        public ParseError(string message = "unspecified.") : base($"Parse error: {message}") { }
    }

    public class ParseErrorStr : ParseError
    {
        public ParseErrorStr(string str) : base(str) { }
    }

    public class SizeMismatch : ParseError
    {
        public ulong RealSize { get; }
        public ulong ReadSize { get; }
        public SizeMismatch(ulong realSize, ulong readSize)
            : base($"expected {realSize} bits, read {readSize}")
        {
            RealSize = realSize;
            ReadSize = readSize;
        }
    }

    public class InvalidId : ParseError
    {
        public int Id { get; }
        public InvalidId(int id)
            : base($"invalid codebook id {id}, try --inline-codebooks")
        {
            Id = id;
        }
    }
} 