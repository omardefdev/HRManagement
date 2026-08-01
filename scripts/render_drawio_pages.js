const fs = require('fs');
const path = require('path');
const { pathToFileURL } = require('url');
const { chromium } = require('playwright');

async function main() {
  const [htmlPath, manifestPath, outputDir] = process.argv.slice(2);
  if (!htmlPath || !manifestPath || !outputDir) {
    throw new Error('Usage: render_drawio_pages.js <html> <manifest.json> <output-dir>');
  }
  const pages = JSON.parse(fs.readFileSync(manifestPath, 'utf8'));
  fs.mkdirSync(outputDir, { recursive: true });
  const executablePath = process.env.PLAYWRIGHT_CHROME_PATH || 'C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe';
  const browser = await chromium.launch({
    headless: true,
    executablePath,
  });
  const context = await browser.newContext({
    viewport: { width: 1500, height: 1350 },
    deviceScaleFactor: 1.5,
  });
  const page = await context.newPage();
  const baseUrl = pathToFileURL(path.resolve(htmlPath));
  for (const item of pages) {
    const url = new URL(baseUrl.href);
    url.searchParams.set('page', String(item.index));
    await page.goto(url.href, { waitUntil: 'load' });
    await page.waitForFunction(() => document.body.dataset.ready === 'true');
    await page.evaluate(() => document.fonts && document.fonts.ready);
    await page.locator('#canvas').screenshot({
      path: path.join(outputDir, item.filename),
      animations: 'disabled',
    });
  }
  await browser.close();
  process.stdout.write(JSON.stringify({ pages: pages.length, outputDir: path.resolve(outputDir) }));
}

main().catch((error) => {
  process.stderr.write(String(error.stack || error));
  process.exit(1);
});
