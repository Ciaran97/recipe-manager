const { chromium } = require(
    process.cwd() + "/tests/e2e/node_modules/@playwright/test",
);
const fs = require("fs");
(async () => {
    const browser = await chromium.launch({ headless: true });
    const page = await browser.newPage({ deviceScaleFactor: 1 });
    const svg = fs.readFileSync("docs/design/brand/app-icon.svg", "utf8");
    fs.writeFileSync("src/RecipeManager.Web/public/favicon.svg", svg);
    let png;
    for (const [size, file] of [
        [
            1024,
            "ios/RecipeManager/Assets.xcassets/AppIcon.appiconset/AppIcon.png",
        ],
        [180, "src/RecipeManager.Web/public/apple-touch-icon.png"],
        [32, undefined],
    ]) {
        await page.setViewportSize({ width: size, height: size });
        await page.setContent(
            `<style>body{margin:0}svg{display:block;width:100vw;height:100vh}</style>${svg}`,
        );
        const rendered = await page.screenshot({ path: file });
        if (size === 32) png = rendered;
    }
    const header = Buffer.alloc(22);
    header.writeUInt16LE(1, 2);
    header.writeUInt16LE(1, 4);
    header[6] = 32;
    header[7] = 32;
    header.writeUInt16LE(1, 10);
    header.writeUInt16LE(32, 12);
    header.writeUInt32LE(png.length, 14);
    header.writeUInt32LE(22, 18);
    fs.writeFileSync(
        "src/RecipeManager.Web/public/favicon.ico",
        Buffer.concat([header, png]),
    );
    await browser.close();
})();
