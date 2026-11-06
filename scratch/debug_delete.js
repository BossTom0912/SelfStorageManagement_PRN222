const { execSync } = require('child_process');

process.env.NODE_TLS_REJECT_UNAUTHORIZED = '0';
const DB_NAME = 'SelfStoragePRN222_Test_20261103_132201';

function runSql(sql) {
  const sanitizedSql = sql.replace(/"/g, '""');
  const cmd = `sqlcmd -S "(localdb)\\mssqllocaldb" -d "${DB_NAME}" -Q "SET NOCOUNT ON; ${sanitizedSql}" -h -1 -W`;
  return execSync(cmd, { encoding: 'utf8' }).trim();
}

function extractInputValue(html, inputName) {
  const escaped = inputName.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
  let m = html.match(new RegExp(`name="${escaped}"[^>]*value="([^"]*)"`, 'i'));
  if (!m) {
    m = html.match(new RegExp(`value="([^"]*)"[^>]*name="${escaped}"`, 'i'));
  }
  return m ? m[1] : '';
}

async function debugDelete() {
  const loginRes = await fetch('https://127.0.0.1:7242/Account/Login');
  const loginHtml = await loginRes.text();
  const token = extractInputValue(loginHtml, '__RequestVerificationToken');
  const loginCookies = loginRes.headers.getSetCookie();

  const postLogin = await fetch('https://127.0.0.1:7242/Account/Login', {
    method: 'POST',
    headers: {
      'Content-Type': 'application/x-www-form-urlencoded',
      'Cookie': loginCookies.map(c => c.split(';')[0]).join('; ')
    },
    body: new URLSearchParams({
      UserName: 'admin@selfstorage.local',
      Password: 'SecretAdminPassword123!',
      __RequestVerificationToken: token
    }).toString(),
    redirect: 'manual'
  });
  const authCookies = [...loginCookies, ...postLogin.headers.getSetCookie()].map(c => c.split(';')[0]).join('; ');

  // Create a clean unit
  const createGet = await fetch('https://127.0.0.1:7242/StorageUnits/Create', {
    headers: { 'Cookie': authCookies }
  });
  const cToken = extractInputValue(await createGet.text(), '__RequestVerificationToken');
  await fetch('https://127.0.0.1:7242/StorageUnits/Create', {
    method: 'POST',
    headers: {
      'Content-Type': 'application/x-www-form-urlencoded',
      'Cookie': authCookies
    },
    body: new URLSearchParams({
      'Input.FacilityId': '1',
      'Input.UnitTypeId': '2',
      'Input.UnitCode': 'DEBUG_DEL_03',
      '__RequestVerificationToken': cToken
    }).toString(),
    redirect: 'manual'
  });

  const unitId = runSql("SELECT id FROM core.storage_units WHERE unit_code='DEBUG_DEL_03'");
  console.log('Created unit ID:', unitId);

  const delGet = await fetch(`https://127.0.0.1:7242/StorageUnits/Delete?id=${unitId}`, {
    headers: { 'Cookie': authCookies }
  });
  const delHtml = await delGet.text();
  const dToken = extractInputValue(delHtml, '__RequestVerificationToken');
  const expVal = extractInputValue(delHtml, 'ExpectedUpdatedAt');
  console.log('Token extracted:', !!dToken);
  console.log('ExpectedUpdatedAt extracted:', expVal);

  const delPost = await fetch(`https://127.0.0.1:7242/StorageUnits/Delete?id=${unitId}`, {
    method: 'POST',
    headers: {
      'Content-Type': 'application/x-www-form-urlencoded',
      'Cookie': authCookies
    },
    body: new URLSearchParams({
      'Id': unitId,
      'ExpectedUpdatedAt': expVal,
      '__RequestVerificationToken': dToken
    }).toString(),
    redirect: 'manual'
  });

  console.log('Delete POST status:', delPost.status);
  console.log('Location:', delPost.headers.get('location'));
  const remainingCount = runSql(`SELECT COUNT(*) FROM core.storage_units WHERE id=${unitId}`);
  console.log('Remaining count in DB:', remainingCount);
}

debugDelete().catch(console.error);
