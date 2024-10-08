﻿// Copyright 2022 Carl Reinke
//
// This file is part of a program that is licensed under the terms of the GNU
// General Public License Version 3 as published by the Free Software
// Foundation.

using System;
using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace SKSshAgent.Ssh;

// https://datatracker.ietf.org/doc/html/rfc4251#section-5
internal ref struct SshWireReader
{
    private ReadOnlySpan<byte> _span;

    public SshWireReader(ReadOnlySpan<byte> source)
    {
        _span = source;
    }

    public int BytesRemaining => _span.Length;

    /// <exception cref="SshWireContentException"/>
    public bool ReadBoolean() => ReadByte() != 0;

    /// <exception cref="SshWireContentException"/>
    public byte ReadByte()
    {
        EnsureLength(1);

        byte value = _span[0];
        _span = _span.Slice(1);
        return value;
    }

    /// <exception cref="ArgumentOutOfRangeException"/>
    /// <exception cref="SshWireContentException"/>
    public ReadOnlySpan<byte> ReadBytes(int length)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(length);

        EnsureLength(length);

        var value = _span.Slice(0, length);
        _span = _span.Slice(length);
        return value;
    }

    /// <exception cref="SshWireContentException"/>
    public uint ReadUInt32()
    {
        EnsureLength(4);

        uint value = BinaryPrimitives.ReadUInt32BigEndian(_span);
        _span = _span.Slice(4);
        return value;
    }

    /// <exception cref="SshWireContentException"/>
    public ReadOnlySpan<byte> ReadByteString()
    {
        EnsureLength(4);

        int length = (int)BinaryPrimitives.ReadUInt32BigEndian(_span);
        if (length < 0)
            throw new SshWireContentException("Excessively long byte string.");

        EnsureLength(4 + length);

        var value = _span.Slice(4, length);
        _span = _span.Slice(4 + length);
        return value;
    }

    /// <exception cref="SshWireContentException"/>
    public string ReadString()
    {
        var bytes = ReadByteString();
        return Encoding.UTF8.GetString(bytes);
    }

    public bool TryReadBoolean(out bool value)
    {
        bool result = TryReadByte(out byte temp);
        value = temp != 0;
        return result;
    }

    public bool TryReadByte(out byte value)
    {
        if (_span.Length >= 1)
        {
            value = _span[0];
            _span = _span.Slice(1);
            return true;
        }

        value = default;
        return false;
    }

    public bool TryReadUInt32(out uint value)
    {
        if (BinaryPrimitives.TryReadUInt32BigEndian(_span, out value))
        {
            _span = _span.Slice(4);
            return true;
        }

        value = default;
        return false;
    }

    /// <exception cref="SshWireContentException"/>
    public bool TryReadByteString(out ReadOnlySpan<byte> value)
    {
        if (BinaryPrimitives.TryReadUInt32BigEndian(_span, out uint temp))
        {
            int length = (int)temp;
            if (length < 0)
                throw new SshWireContentException("Excessively long byte string.");

            if (_span.Length - 4 >= length)
            {
                value = _span.Slice(4, length);
                _span = _span.Slice(4 + length);
                return true;
            }
        }

        value = default;
        return false;
    }

    /// <exception cref="SshWireContentException"/>
    public bool TryReadString([MaybeNullWhen(false)] out string value)
    {
        if (TryReadByteString(out var bytes))
        {
            value = Encoding.UTF8.GetString(bytes);
            return true;
        }

        value = default;
        return false;
    }

    /// <exception cref="SshWireContentException"/>
    private readonly void EnsureLength(int length)
    {
        if (_span.Length < length)
            throw new SshWireContentException("Insufficient data.");
    }
}
