const { execSync } = require('child_process');

process.env.NODE_TLS_REJECT_UNAUTHORIZED = '0';
const DB_NAME = 'SelfStoragePRN222_Test_20261103_132201';

function runSql(sql) {
  const sanitizedSql = sql.replace(/"/g, '""');
  const cmd = `sqlcmd -S "(localdb)\\mssqllocaldb" -d "${DB_NAME}" -Q "SET QUOTED_IDENTIFIER ON; SET ANSI_NULLS ON; SET NOCOUNT ON; ${sanitizedSql}" -h -1 -W`;
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

async function testUnlist() {
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

  // 1. Create unit
  const cGet = await fetch('https://127.0.0.1:7242/StorageUnits/Create', { headers: { 'Cookie': authCookies } });
  const cToken = extractInputValue(await cGet.text(), '__RequestVerificationToken');
  await fetch('https://127.0.0.1:7242/StorageUnits/Create', {
    method: 'POST',
    headers: {
      'Content-Type': 'application/x-www-form-urlencoded',
      'Cookie': authCookies
    },
    body: new URLSearchParams({
      'Input.FacilityId': '1',
      'Input.UnitTypeId': '2',
      'Input.UnitCode': 'A2_TEST_UNLIST_02',
      '__RequestVerificationToken': cToken
    }).toString(),
    redirect: 'manual'
  });

  const unitId = runSql("SELECT id FROM core.storage_units WHERE unit_code='A2_TEST_UNLIST_02'");
  console.log('Created unit ID:', unitId);

  // 2. Add status history
  runSql(`INSERT INTO core.unit_status_history (storage_unit_id, old_status, new_status, changed_by, reason, changed_at) VALUES (${unitId}, 'available', 'cleaning', 7, 'Audit reference history', SYSDATETIMEOFFSET())`);
  const histCount = runSql(`SELECT COUNT(*) FROM core.unit_status_history WHERE storage_unit_id=${unitId}`);
  console.log('Status history count:', histCount);

  // 3. GET Delete page
  const delGet = await fetch(`https://127.0.0.1:7242/StorageUnits/Delete?id=${unitId}`, {
    headers: { 'Cookie': authCookies }
  });
  const delHtml = await delGet.text();
  console.log('Has unlist warning:', delHtml.includes('ngừng niêm yết'));
  console.log('Submit button text in HTML:', delHtml.match(/btnSubmitText[^>]*>([^<]+)</)?.[1]);

  const dToken = extractInputValue(delHtml, '__RequestVerificationToken');
  const expVal = extractInputValue(delHtml, 'ExpectedUpdatedAt');

  // 4. POST Delete
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

  // 5. Verify unit in DB
  const isListed = runSql(`SELECT is_listed FROM core.storage_units WHERE id=${unitId}`);
  console.log('is_listed in DB after delete:', isListed);

  runSql(`DELETE FROM core.unit_status_history WHERE storage_unit_id=${unitId}`);
  runSql(`DELETE FROM core.storage_units WHERE id=${unitId}`);
}

testUnlist().catch(console.error);
