type LicenseAction = "activate" | "deactivate";

type LicenseRequest = {
  key?: unknown;
  instanceName?: unknown;
  instanceId?: unknown;
};

function json(body: Record<string, unknown>, status = 200): Response {
  return Response.json(body, {
    status,
    headers: { "Cache-Control": "no-store" },
  });
}

async function readLimitedJson(request: Request): Promise<LicenseRequest> {
  const reader = request.body?.getReader();
  if (!reader) return {};

  const chunks: Uint8Array[] = [];
  let size = 0;
  while (true) {
    const { done, value } = await reader.read();
    if (done) break;
    size += value.byteLength;
    if (size > 8192) {
      await reader.cancel();
      throw Object.assign(new Error("Request too large."), { status: 413 });
    }
    chunks.push(value);
  }

  const bytes = new Uint8Array(size);
  let offset = 0;
  for (const chunk of chunks) {
    bytes.set(chunk, offset);
    offset += chunk.byteLength;
  }
  let value: unknown;
  try {
    value = JSON.parse(new TextDecoder().decode(bytes) || "{}");
  } catch {
    throw Object.assign(new Error("Invalid JSON request body."), { status: 400 });
  }
  if (typeof value !== "object" || value === null || Array.isArray(value)) {
    throw Object.assign(new Error("Invalid request body."), { status: 400 });
  }
  return value as LicenseRequest;
}

async function callCreem(path: string, body: Record<string, string>): Promise<Record<string, unknown>> {
  const apiKey = process.env.CREEM_API_KEY;
  if (!apiKey) throw Object.assign(new Error("License activation is not configured yet."), { status: 503 });

  const base = (process.env.CREEM_API_BASE || "https://api.creem.io").replace(/\/$/, "");
  const response = await fetch(`${base}${path}`, {
    method: "POST",
    headers: { "Content-Type": "application/json", "x-api-key": apiKey },
    body: JSON.stringify(body),
    cache: "no-store",
    signal: AbortSignal.timeout(15000),
  });
  const result: unknown = await response.json().catch(() => ({}));
  if (!response.ok) {
    const responseObject = typeof result === "object" && result !== null ? result as Record<string, unknown> : {};
    const message = typeof responseObject.message === "string" ? responseObject.message
      : typeof responseObject.error === "string" ? responseObject.error
      : `Creem request failed (${response.status}).`;
    throw Object.assign(new Error(message), { status: response.status >= 500 ? 502 : response.status });
  }
  if (typeof result !== "object" || result === null || Array.isArray(result)) {
    throw Object.assign(new Error("Creem returned an invalid response."), { status: 502 });
  }
  return result as Record<string, unknown>;
}

export async function handleLicenseRequest(request: Request, action: LicenseAction): Promise<Response> {
  try {
    const data = await readLimitedJson(request);
    if (typeof data.key !== "string" || data.key.trim().length < 6) {
      return json({ error: "Enter a valid license key." }, 400);
    }

    if (action === "activate") {
      if (typeof data.instanceName !== "string" || !data.instanceName.trim() || data.instanceName.length > 120) {
        return json({ error: "Invalid device activation name." }, 400);
      }
      const result = await callCreem("/v1/licenses/activate", {
        key: data.key.trim(),
        instance_name: data.instanceName.trim(),
      });
      if (result.status !== "active") return json({ error: "This license is not active. Check the key or contact support." }, 403);

      const instance = typeof result.instance === "object" && result.instance !== null
        ? result.instance as Record<string, unknown>
        : {};
      const instanceId = instance.id ?? result.instance_id ?? result.id;
      if (typeof instanceId !== "string" || !instanceId) {
        return json({ error: "Creem did not return an activation ID." }, 502);
      }
      return json({
        instanceId,
        status: result.status,
        activationCount: result.activation,
        activationLimit: result.activation_limit,
      });
    }

    if (typeof data.instanceId !== "string" || !data.instanceId.trim()) {
      return json({ error: "Missing activation ID." }, 400);
    }
    const result = await callCreem("/v1/licenses/deactivate", {
      key: data.key.trim(),
      instance_id: data.instanceId.trim(),
    });
    return json({ success: result.success ?? true });
  } catch (error) {
    const failure = error as Error & { status?: number };
    return json({ error: failure.message || "License request failed." }, failure.status || 502);
  }
}
