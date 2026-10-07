# Windows CI cancellation with Unity CLI

Known limitation, verified locally on 2026-09-16 with standalone Unity CLI
**1.0.0-beta.9** and **1.0.0-beta.10**, Unity Editor **6000.3.23f1**, and
64-bit Windows. The generated `unity run` command remains unchanged.

## Symptom and confirmed behavior

Aborting a CI job can leave its Unity Editor running and holding the project
open. A subsequent build using that checkout then fails because Unity already
has the project open.

In controlled local tests, Ctrl+C stopped `unity run` but left its Editor alive
at the 1-, 5- and 10-second checks. This reproduced on both tested CLI versions
using an Editor method that simply waited; Build Forge was not invoked.
Launching the Editor directly and delivering Ctrl+C stopped it within the
first one-second check.

| Local cancellation test | CLI beta.9 | CLI beta.10 |
| --- | --- | --- |
| Ctrl+C to CLI console | Editor survives | Editor survives |
| Batch command → CLI → Editor; WinP soft kill, then root-process tree cleanup | Editor survives | Editor survives |
| Follow-up cleanup matching the job marker on the Editor | Editor stops | Editor stops |

The latter two stages were simulated with Jenkins' Windows process library,
WinP **1.31**, under 64-bit Java **17.0.18**. Before cancellation, the Editor
was a direct child of the CLI and retained the synthetic `BUILD_ID`,
`BUILD_NUMBER`, `JENKINS_NODE_COOKIE`, `HUDSON_COOKIE` and `WORKSPACE` values.
The helper verified ownership before targeting either test process.

## What remains unconfirmed

A Jenkins Freestyle job was reported to leave Unity running after aborting a
generated Build Forge command. The local simulation's additional job-marker
cleanup successfully stopped the Editor. Why that cleanup missed the Editor
on the reported Jenkins host is not established: the actual abort log and
agent cleanup diagnostics are still needed.

These were local process-control tests, not runs on a Jenkins server. They do
not establish behavior under another service account, Jenkins/JDK/WinP version,
or on macOS/Linux. Upgrading from CLI beta.9 to beta.10 did not resolve the
local reproduction; later versions need their own cancellation check.

## Handling an affected job

Keep using the generated CI command. If a cancelled job leaves Unity running,
identify that job's Editor by its PID and project path and stop it and its
remaining build processes before reusing the checkout. Avoid terminating all
Unity processes by name or deleting the project lock while its Editor is alive.
Retain the aborted console output, CLI version and agent cleanup errors when
investigating a recurrence.

Build Forge's synchronous entry point and explicit Editor exit handle normal
completion and managed build failures. `-quit` controls normal shutdown; it
does not guarantee cancellation reaches the Editor. Forced termination and
Editor crashes can prevent settings restoration from running, so inspect and
recover the affected workspace, or use a fresh checkout, before retrying.

## Evidence and references

The raw evidence for these runs (process IDs, observations and logs) and the probe
sources are retained by the maintainer and not distributed with this package. No
game settings were changed by these tests; surviving probe Editors were released
after observation.

See [Jenkins build-abort behavior](https://www.jenkins.io/doc/book/using/aborting-a-build/),
its [process cleanup implementation](https://github.com/jenkinsci/jenkins/blob/master/core/src/main/java/hudson/util/ProcessTree.java),
and the [WinP process-control API](https://javadoc.jenkins.io/component/winp/org/jvnet/winp/WinProcess.html).
