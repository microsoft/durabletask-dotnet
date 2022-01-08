// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace WebAPI.Models;

public class OrderStatus
{
    public bool RequiresApproval { get; set; }

    public ApprovalEvent? Approval { get; set; }
}
