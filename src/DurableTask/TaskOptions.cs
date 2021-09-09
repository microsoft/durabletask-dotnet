using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using DurableTask.Core;
using DurableTask.Core.Serializing;

namespace DurableTask;

public class TaskOptions
{
    public RetryOptions? RetryOptions { get; set; }

    public DataConverter? DataConverter { get; set; }
}

