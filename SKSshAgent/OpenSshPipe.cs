﻿// Copyright 2022 Carl Reinke
//
// This file is part of a program that is licensed under the terms of the GNU
// General Public License Version 3 as published by the Free Software
// Foundation.

namespace SKSshAgent;

internal sealed class OpenSshPipe : SshAgentPipe
{
    public OpenSshPipe(KeyListForm form)
        : base(form)
    {
    }

    public override string PipeName => @"openssh-ssh-agent";
}
