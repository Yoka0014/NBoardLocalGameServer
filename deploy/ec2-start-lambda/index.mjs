// AWS Lambda handler (Node.js 20.x runtime) exposed via a Function URL.
// The only job of this function is to let a static, publicly-hosted frontend (which cannot hold AWS
// credentials) start/check the match-server EC2 instance without exposing the instance's own API.
//
// Actions (both are GET, via ?action=... on the Function URL):
//   ?action=start   -> calls ec2:StartInstances for INSTANCE_ID, idempotent (no-op if already running)
//   ?action=status  -> calls ec2:DescribeInstances, returns { state: "running" | "stopped" | "pending" | ... }
//
// Configure via Lambda environment variables:
//   INSTANCE_ID  (required) - the EC2 instance to control, e.g. "i-0123456789abcdef0"
//   SHARED_TOKEN (optional) - if set, callers must pass ?token=<value> matching this, as a minimal
//                             deterrent against random internet scanners hitting the URL. This is
//                             NOT real authentication (it's visible to anyone reading the frontend's
//                             JS) -- the actual safety net is the IAM policy below, which only allows
//                             this function to Start/Describe the one configured instance and nothing
//                             else, so the worst outcome of abuse is "someone starts your instance."

import { EC2Client, StartInstancesCommand, DescribeInstancesCommand } from "@aws-sdk/client-ec2";

const ec2 = new EC2Client({});

export const handler = async (event) => {
  const cors = {
    "Access-Control-Allow-Origin": "*",
    "Access-Control-Allow-Methods": "GET,OPTIONS",
    "Access-Control-Allow-Headers": "content-type"
  };

  if (event.requestContext?.http?.method === "OPTIONS") {
    return { statusCode: 204, headers: cors, body: "" };
  }

  const qs = event.queryStringParameters || {};
  const instanceId = process.env.INSTANCE_ID;
  const requiredToken = process.env.SHARED_TOKEN;

  if (!instanceId) {
    return json(500, { error: "Lambda is misconfigured: INSTANCE_ID environment variable is not set." }, cors);
  }
  if (requiredToken && qs.token !== requiredToken) {
    return json(403, { error: "Missing or invalid token." }, cors);
  }

  try {
    if (qs.action === "start") {
      await ec2.send(new StartInstancesCommand({ InstanceIds: [instanceId] }));
      return json(200, { ok: true, action: "start", instanceId }, cors);
    }

    if (qs.action === "status") {
      const result = await ec2.send(new DescribeInstancesCommand({ InstanceIds: [instanceId] }));
      const state = result.Reservations?.[0]?.Instances?.[0]?.State?.Name ?? "unknown";
      return json(200, { ok: true, instanceId, state }, cors);
    }

    return json(400, { error: 'Missing or unknown ?action= (expected "start" or "status").' }, cors);
  } catch (err) {
    return json(502, { error: String(err?.message ?? err) }, cors);
  }
};

function json(statusCode, body, headers) {
  return {
    statusCode,
    headers: { "Content-Type": "application/json", ...headers },
    body: JSON.stringify(body)
  };
}
