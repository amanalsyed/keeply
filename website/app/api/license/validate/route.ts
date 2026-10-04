import { handleLicenseRequest } from "../../../../lib/license";

export const runtime = "nodejs";

export async function POST(request: Request): Promise<Response> {
  return handleLicenseRequest(request, "validate");
}
