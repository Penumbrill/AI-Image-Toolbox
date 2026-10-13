// Algorithm baseline; BSD-2-Clause traversal license: see THIRD_PARTY_NOTICES.md.
function gilbert2dIterative(width, height) {
    const coordinates = [];
    
    const stack = [];
    
    if (width >= height) {
        stack.push({x: 0, y: 0, ax: width, ay: 0, bx: 0, by: height});
    } else {
        stack.push({x: 0, y: 0, ax: 0, ay: height, bx: width, by: 0});
    }
    
    while (stack.length > 0) {
        const {x, y, ax, ay, bx, by} = stack.pop();
        
        const w = Math.abs(ax + ay);
        const h = Math.abs(bx + by);
        
        const dax = Math.sign(ax), day = Math.sign(ay);
        const dbx = Math.sign(bx), dby = Math.sign(by);
        
        if (h === 1) {
            let curX = x, curY = y;
            for (let i = 0; i < w; i++) {
                coordinates.push([curX, curY]);
                curX += dax;
                curY += day;
            }
            continue;
        }
        
        if (w === 1) {
            let curX = x, curY = y;
            for (let i = 0; i < h; i++) {
                coordinates.push([curX, curY]);
                curX += dbx;
                curY += dby;
            }
            continue;
        }
        
        let ax2 = Math.floor(ax / 2), ay2 = Math.floor(ay / 2);
        let bx2 = Math.floor(bx / 2), by2 = Math.floor(by / 2);
        
        const w2 = Math.abs(ax2 + ay2);
        const h2 = Math.abs(bx2 + by2);
        
        if (2 * w > 3 * h) {
            if ((w2 % 2) && (w > 2)) {
                ax2 += dax;
                ay2 += day;
            }
            
            stack.push({x: x + ax2, y: y + ay2, ax: ax - ax2, ay: ay - ay2, bx, by});
            stack.push({x, y, ax: ax2, ay: ay2, bx, by});
        } else {
            if ((h2 % 2) && (h > 2)) {
                bx2 += dbx;
                by2 += dby;
            }
            
            stack.push({x: x + (ax - dax) + (bx2 - dbx), y: y + (ay - day) + (by2 - dby),
                ax: -bx2, ay: -by2, bx: -(ax - ax2), by: -(ay - ay2)});
            stack.push({x: x + bx2, y: y + by2, ax, ay, bx: bx - bx2, by: by - by2});
            stack.push({x, y, ax: bx2, ay: by2, bx: ax2, by: ay2});
        }
    }
    
    return coordinates;
}

function rgbToHsl(r, g, b) {
    r /= 255, g /= 255, b /= 255;
    const max = Math.max(r, g, b), min = Math.min(r, g, b);
    let h, s, l = (max + min) / 2;

    if (max === min) {
        h = s = 0;
    } else {
        const d = max - min;
        s = l > 0.5 ? d / (2 - max - min) : d / (max + min);
        switch (max) {
            case r: h = (g - b) / d + (g < b ? 6 : 0); break;
            case g: h = (b - r) / d + 2; break;
            case b: h = (r - g) / d + 4; break;
        }
        h /= 6;
    }

    return [h * 360, s * 100, l * 100];
}

function adjustGreenColor(data, enhance = true) {
    const result = new Uint8ClampedArray(data.length);
    
    for (let i = 0; i < data.length; i += 4) {
        const r = data[i];
        const g = data[i + 1];
        const b = data[i + 2];
        const a = data[i + 3];
        
        const [h, s, l] = rgbToHsl(r, g, b);
        
        let newR = r, newG = g, newB = b;
        
        const isGreenish = (h >= 75 && h <= 165 && s > 8 && l > 10 && l < 90);
        
        if (enhance && isGreenish) {
            const greenFactor = 1.0 + (Math.min(s, 70) / 100) * 0.6;
            const redBlueFactor = Math.max(0.7, 1.0 - (Math.min(s, 70) / 100) * 0.3);
            
            newG = Math.min(255, Math.round(g * greenFactor));
            newR = Math.max(0, Math.round(r * redBlueFactor));
            newB = Math.max(0, Math.round(b * redBlueFactor));
            
        } else if (!enhance && isGreenish) {
            const greenFactor = Math.max(0.5, 1.0 - (Math.min(s, 80) / 100) * 0.5);
            const redBlueFactor = Math.min(1.3, 1.0 + (Math.min(s, 80) / 100) * 0.3);
            
            newG = Math.max(0, Math.round(g * greenFactor));
            newR = Math.min(255, Math.round(r * redBlueFactor));
            newB = Math.min(255, Math.round(b * redBlueFactor));
        }
        
        result[i] = Math.max(0, Math.min(255, newR));
        result[i + 1] = Math.max(0, Math.min(255, newG));
        result[i + 2] = Math.max(0, Math.min(255, newB));
        result[i + 3] = a;
    }
    
    return result;
}
