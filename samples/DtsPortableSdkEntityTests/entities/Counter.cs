﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Entities;

namespace DtsPortableSdkEntityTests;

class Counter : TaskEntity<int>
{
    public void Increment()
    {
        this.State++;
    }

    public void Add(int amount)
    {
        this.State += amount;
    }

    public int Get()
    {
        return this.State;
    }

    public void Set(int value)
    {
        this.State = value;
    }

    public static void Register(DurableTaskRegistry r)
    {
        r.AddEntity(nameof(Counter), _ => new Counter());
    }
}
