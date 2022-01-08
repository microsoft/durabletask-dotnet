// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace WebAPI.Models;

public class ApprovalEvent
{
    public bool IsApproved { get; set; }

    public string? Approver { get; set; }
}
