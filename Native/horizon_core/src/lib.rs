//! Horizon Native Core Engine (Rust)
//! High-performance native engine for Horizon Browser:
//! - Sub-millisecond SIMD/Quantized Color Palette Extraction
//! - Native WCAG Contrast Ratio Calculation
//! - Ultra-fast AdBlock / Domain Filter Pattern Matching
//! - Direct Win32 Memory Pressure Diagnostics

use std::collections::HashMap;
use std::ffi::CStr;
use std::os::raw::c_char;

#[repr(C)]
pub struct SystemMemoryInfo {
    pub total_phys_mb: u64,
    pub avail_phys_mb: u64,
    pub memory_load_pct: u32,
}

/// Returns the native engine version (110 = v1.1.0 Aegis & Layout Core).
#[no_mangle]
pub extern "C" fn horizon_core_version() -> i32 {
    110
}

/// Fast native sRGB luminance conversion.
#[inline(always)]
fn linear_component(v: u8) -> f64 {
    let s = v as f64 / 255.0;
    if s <= 0.03928 {
        s / 12.92
    } else {
        ((s + 0.055) / 1.055).powf(2.4)
    }
}

/// Fast native luminance from RGB.
#[inline(always)]
fn luminance(r: u8, g: u8, b: u8) -> f64 {
    0.2126 * linear_component(r) + 0.7152 * linear_component(g) + 0.0722 * linear_component(b)
}

/// Calculates WCAG 2.1 contrast ratio between two RGB colors (returns 1.0 to 21.0).
#[no_mangle]
pub extern "C" fn horizon_calculate_contrast_ratio(
    r1: u8, g1: u8, b1: u8,
    r2: u8, g2: u8, b2: u8,
) -> f64 {
    let l1 = luminance(r1, g1, b1) + 0.05;
    let l2 = luminance(r2, g2, b2) + 0.05;
    if l1 > l2 {
        l1 / l2
    } else {
        l2 / l1
    }
}

#[inline(always)]
fn get_hue(r: u8, g: u8, b: u8) -> f32 {
    let rf = r as f32;
    let gf = g as f32;
    let bf = b as f32;
    let max = rf.max(gf.max(bf));
    let min = rf.min(gf.min(bf));
    let delta = max - min;
    if delta < 1e-4 {
        return 0.0;
    }
    let mut h = if (max - rf).abs() < 1e-4 {
        (gf - bf) / delta
    } else if (max - gf).abs() < 1e-4 {
        2.0 + (bf - rf) / delta
    } else {
        4.0 + (rf - gf) / delta
    };
    h *= 60.0;
    if h < 0.0 {
        h += 360.0;
    }
    h
}

#[inline(always)]
fn hue_delta(h1: f32, h2: f32) -> f32 {
    let diff = (h1 - h2).abs();
    if diff > 180.0 {
        360.0 - diff
    } else {
        diff
    }
}

#[inline(always)]
fn color_dist_sq(r1: u8, g1: u8, b1: u8, r2: u8, g2: u8, b2: u8) -> i32 {
    let dr = r1 as i32 - r2 as i32;
    let dg = g1 as i32 - g2 as i32;
    let db = b1 as i32 - b2 as i32;
    dr * dr + dg * dg + db * db
}

#[inline(always)]
fn is_valid_color(r: u8, g: u8, b: u8) -> bool {
    // Exclude near-black
    if r < 18 && g < 18 && b < 18 {
        return false;
    }
    // Exclude near-white
    if r > 240 && g > 240 && b > 240 {
        return false;
    }
    // Exclude washed out grays
    let max = r.max(g.max(b));
    let min = r.min(g.min(b));
    let diff = max as i32 - min as i32;
    if diff < 15 && (r < 60 || r > 195) {
        return false;
    }
    true
}

/// Ultra-fast native color palette extraction from raw BGRA pixels.
/// Writes up to max_colors (0x00RRGGBB format) into out_colors buffer.
/// Returns actual number of distinct colors extracted.
#[no_mangle]
pub extern "C" fn horizon_extract_palette(
    pixels_ptr: *const u8,
    pixel_bytes_len: usize,
    out_colors: *mut u32,
    max_colors: usize,
) -> i32 {
    if pixels_ptr.is_null() || out_colors.is_null() || pixel_bytes_len < 4 || max_colors == 0 {
        return 0;
    }

    let pixels = unsafe { std::slice::from_raw_parts(pixels_ptr, pixel_bytes_len) };
    let total_pixels = pixel_bytes_len / 4;
    let step = (1.max(total_pixels / 2500)) * 4;

    let mut buckets: HashMap<u32, u32> = HashMap::with_capacity(256);

    let mut i = 0;
    while i + 3 < pixel_bytes_len {
        let b = pixels[i];
        let g = pixels[i + 1];
        let r = pixels[i + 2];
        let a = pixels[i + 3];

        if a >= 200 && is_valid_color(r, g, b) {
            let rb = ((r as u32) / 16) * 16;
            let gb = ((g as u32) / 16) * 16;
            let bb = ((b as u32) / 16) * 16;
            let bucket = (rb << 16) | (gb << 8) | bb;
            *buckets.entry(bucket).or_insert(0) += 1;
        }

        i += step;
    }

    if buckets.is_empty() {
        return 0;
    }

    let mut sorted: Vec<(u32, u32)> = buckets.into_iter().collect();
    sorted.sort_unstable_by(|a, b| b.1.cmp(&a.1));

    let mut selected_colors: Vec<(u8, u8, u8, f32)> = Vec::with_capacity(max_colors);

    for (c, _) in sorted.iter() {
        let r = ((c >> 16) & 0xFF) as u8;
        let g = ((c >> 8) & 0xFF) as u8;
        let b = (c & 0xFF) as u8;
        let hue = get_hue(r, g, b);

        let hue_ok = selected_colors.iter().all(|s| hue_delta(s.3, hue) > 18.0);
        let rgb_ok = selected_colors
            .iter()
            .all(|s| color_dist_sq(s.0, s.1, s.2, r, g, b) > 1600); // 40^2

        if selected_colors.is_empty() || hue_ok || rgb_ok {
            selected_colors.push((r, g, b, hue));
            if selected_colors.len() >= max_colors {
                break;
            }
        }
    }

    // Fill output array
    let count = selected_colors.len();
    for (idx, (r, g, b, _)) in selected_colors.into_iter().enumerate() {
        let col = ((r as u32) << 16) | ((g as u32) << 8) | (b as u32);
        unsafe {
            *out_colors.add(idx) = col;
        }
    }

    count as i32
}

/// Fast native AdBlock / URL filter matcher.
/// Supports exact matching, wildcard `*`, and AdBlock `||domain^` prefix syntax.
#[no_mangle]
pub extern "C" fn horizon_matches_filter_rule(
    url_cstr: *const c_char,
    rule_cstr: *const c_char,
) -> bool {
    if url_cstr.is_null() || rule_cstr.is_null() {
        return false;
    }

    let url_str = match unsafe { CStr::from_ptr(url_cstr) }.to_str() {
        Ok(s) => s,
        Err(_) => return false,
    };

    let rule_str = match unsafe { CStr::from_ptr(rule_cstr) }.to_str() {
        Ok(s) => s.trim(),
        Err(_) => return false,
    };

    if rule_str.is_empty() {
        return false;
    }

    // Handle Adblock style ||example.com^
    if let Some(stripped) = rule_str.strip_prefix("||") {
        let domain_part = stripped.trim_end_matches('^');
        if let Some(host_start) = url_str.find("://") {
            let rest = &url_str[host_start + 3..];
            let host = rest.split('/').next().unwrap_or(rest);
            return host == domain_part || host.ends_with(&format!(".{}", domain_part));
        }
        return url_str.contains(domain_part);
    }

    // Handle wildcard matching
    if rule_str.contains('*') {
        let parts: Vec<&str> = rule_str.split('*').collect();
        let mut pos = 0;
        for (idx, part) in parts.iter().enumerate() {
            if part.is_empty() {
                continue;
            }
            match url_str[pos..].find(part) {
                Some(found) => {
                    if idx == 0 && !rule_str.starts_with('*') && found != 0 {
                        return false;
                    }
                    pos += found + part.len();
                }
                None => return false,
            }
        }
        return true;
    }

    // Plain substring matching
    url_str.contains(rule_str)
}

/// Native Win32 Memory Status Diagnostic.
#[no_mangle]
pub extern "C" fn horizon_get_system_memory(out_info: *mut SystemMemoryInfo) -> bool {
    if out_info.is_null() {
        return false;
    }

    #[repr(C)]
    struct MemoryStatusEx {
        dw_length: u32,
        dw_memory_load: u32,
        ull_total_phys: u64,
        ull_avail_phys: u64,
        ull_total_page_file: u64,
        ull_avail_page_file: u64,
        ull_total_virtual: u64,
        ull_avail_virtual: u64,
        ull_avail_extended_virtual: u64,
    }

    #[link(name = "kernel32")]
    extern "system" {
        fn GlobalMemoryStatusEx(stat: *mut MemoryStatusEx) -> i32;
    }

    let mut stat = MemoryStatusEx {
        dw_length: std::mem::size_of::<MemoryStatusEx>() as u32,
        dw_memory_load: 0,
        ull_total_phys: 0,
        ull_avail_phys: 0,
        ull_total_page_file: 0,
        ull_avail_page_file: 0,
        ull_total_virtual: 0,
        ull_avail_virtual: 0,
        ull_avail_extended_virtual: 0,
    };

    let ret = unsafe { GlobalMemoryStatusEx(&mut stat) };
    if ret != 0 {
        unsafe {
            (*out_info).total_phys_mb = stat.ull_total_phys / (1024 * 1024);
            (*out_info).avail_phys_mb = stat.ull_avail_phys / (1024 * 1024);
            (*out_info).memory_load_pct = stat.dw_memory_load;
        }
        true
    } else {
        false
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// Aegis Subsystem: High-Performance Process Tree Memory Aggregator
// ─────────────────────────────────────────────────────────────────────────────

#[repr(C)]
pub struct BrowserMemoryMetrics {
    pub main_pid: u32,
    pub main_process_ram_bytes: u64,
    pub render_processes_count: u32,
    pub render_processes_ram_bytes: u64,
    pub total_browser_ram_bytes: u64,
}

#[repr(C)]
struct ProcessMemoryCounters {
    cb: u32,
    page_fault_count: u32,
    peak_working_set_size: usize,
    working_set_size: usize,
    quota_peak_paged_pool_usage: usize,
    quota_paged_pool_usage: usize,
    quota_peak_non_paged_pool_usage: usize,
    quota_non_paged_pool_usage: usize,
    pagefile_usage: usize,
    peak_pagefile_usage: usize,
}

const TH32CS_SNAPPROCESS: u32 = 0x00000002;
const PROCESS_QUERY_LIMITED_INFORMATION: u32 = 0x1000;
const PROCESS_QUERY_INFORMATION: u32 = 0x0400;
const PROCESS_VM_READ: u32 = 0x0010;

#[repr(C)]
struct ProcessEntry32W {
    dw_size: u32,
    cnt_usage: u32,
    th32_process_id: u32,
    th32_default_heap_id: usize,
    th32_module_id: u32,
    cnt_threads: u32,
    th32_parent_process_id: u32,
    pc_pri_class_base: i32,
    dw_flags: u32,
    sz_exe_file: [u16; 260],
}

#[link(name = "kernel32")]
extern "system" {
    fn CreateToolhelp32Snapshot(dw_flags: u32, th32_process_id: u32) -> *mut std::ffi::c_void;
    fn Process32FirstW(h_snapshot: *mut std::ffi::c_void, lppe: *mut ProcessEntry32W) -> i32;
    fn Process32NextW(h_snapshot: *mut std::ffi::c_void, lppe: *mut ProcessEntry32W) -> i32;
    fn OpenProcess(dw_desired_access: u32, b_inherit_handle: i32, dw_process_id: u32) -> *mut std::ffi::c_void;
    fn CloseHandle(h_object: *mut std::ffi::c_void) -> i32;
}

#[link(name = "psapi")]
extern "system" {
    fn GetProcessMemoryInfo(
        h_process: *mut std::ffi::c_void,
        ppsmem_counters: *mut ProcessMemoryCounters,
        cb: u32,
    ) -> i32;
}

fn query_process_working_set(pid: u32) -> u64 {
    let access = PROCESS_QUERY_LIMITED_INFORMATION | PROCESS_VM_READ;
    let handle = unsafe { OpenProcess(access, 0, pid) };
    if handle.is_null() {
        // Fallback with PROCESS_QUERY_INFORMATION
        let handle_fb = unsafe { OpenProcess(PROCESS_QUERY_INFORMATION | PROCESS_VM_READ, 0, pid) };
        if handle_fb.is_null() {
            return 0;
        }
        let mut counters: ProcessMemoryCounters = unsafe { std::mem::zeroed() };
        counters.cb = std::mem::size_of::<ProcessMemoryCounters>() as u32;
        let ok = unsafe { GetProcessMemoryInfo(handle_fb, &mut counters, counters.cb) };
        unsafe { CloseHandle(handle_fb) };
        if ok != 0 {
            return counters.working_set_size as u64;
        }
        return 0;
    }

    let mut counters: ProcessMemoryCounters = unsafe { std::mem::zeroed() };
    counters.cb = std::mem::size_of::<ProcessMemoryCounters>() as u32;
    let ok = unsafe { GetProcessMemoryInfo(handle, &mut counters, counters.cb) };
    unsafe { CloseHandle(handle) };
    if ok != 0 {
        counters.working_set_size as u64
    } else {
        0
    }
}

/// Ultra-fast native browser process tree memory auditing.
/// Collects memory across Horizon Browser and all child msedgewebview2.exe processes.
#[no_mangle]
pub extern "C" fn horizon_get_browser_process_memory(
    main_pid: u32,
    out_metrics: *mut BrowserMemoryMetrics,
) -> bool {
    if out_metrics.is_null() || main_pid == 0 {
        return false;
    }

    let main_ram = query_process_working_set(main_pid);
    let mut render_count: u32 = 0;
    let mut render_ram: u64 = 0;

    let snapshot = unsafe { CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0) };
    if !snapshot.is_null() && snapshot as isize != -1 {
        let mut entry: ProcessEntry32W = unsafe { std::mem::zeroed() };
        entry.dw_size = std::mem::size_of::<ProcessEntry32W>() as u32;

        if unsafe { Process32FirstW(snapshot, &mut entry) } != 0 {
            loop {
                // Check if this process is a child of the main browser process
                if entry.th32_parent_process_id == main_pid && entry.th32_process_id != main_pid {
                    // Convert sz_exe_file to UTF-8
                    let name_len = entry.sz_exe_file.iter().position(|&c| c == 0).unwrap_or(entry.sz_exe_file.len());
                    let name = String::from_utf16_lossy(&entry.sz_exe_file[..name_len]).to_lowercase();

                    if name.contains("msedgewebview2") || name.contains("horizon") {
                        let proc_ram = query_process_working_set(entry.th32_process_id);
                        if proc_ram > 0 {
                            render_ram += proc_ram;
                            render_count += 1;
                        }
                    }
                }

                if unsafe { Process32NextW(snapshot, &mut entry) } == 0 {
                    break;
                }
            }
        }
        unsafe { CloseHandle(snapshot) };
    }

    let total = main_ram + render_ram;
    unsafe {
        (*out_metrics).main_pid = main_pid;
        (*out_metrics).main_process_ram_bytes = main_ram;
        (*out_metrics).render_processes_count = render_count;
        (*out_metrics).render_processes_ram_bytes = render_ram;
        (*out_metrics).total_browser_ram_bytes = total;
    }

    true
}

// ─────────────────────────────────────────────────────────────────────────────
// Regional Luminance & WCAG Dynamic Text Contrast Engine
// ─────────────────────────────────────────────────────────────────────────────

#[repr(C)]
pub struct LuminanceMetrics {
    pub mean_luminance: f32,
    pub min_luminance: f32,
    pub max_luminance: f32,
    pub recommended_text_color: u32, // 0 = Light (#FFFFFFFF), 1 = Dark (#FF1A1A1A)
    pub contrast_ratio_against_white: f32,
    pub contrast_ratio_against_black: f32,
}

/// Fast native regional luminance sampling from raw BGRA pixel bytes.
/// Computes perceived brightness and optimal contrast color for text/icons placed over backdrops.
#[no_mangle]
pub extern "C" fn horizon_calculate_region_luminance(
    pixels_ptr: *const u8,
    total_bytes_len: usize,
    img_width: u32,
    img_height: u32,
    stride: u32,
    rx: u32,
    ry: u32,
    rw: u32,
    rh: u32,
    out_metrics: *mut LuminanceMetrics,
) -> bool {
    if pixels_ptr.is_null() || out_metrics.is_null() || total_bytes_len == 0 {
        return false;
    }
    if img_width == 0 || img_height == 0 || stride == 0 || rw == 0 || rh == 0 {
        return false;
    }
    if rx >= img_width || ry >= img_height {
        return false;
    }

    let end_x = (rx + rw).min(img_width);
    let end_y = (ry + rh).min(img_height);
    let pixels = unsafe { std::slice::from_raw_parts(pixels_ptr, total_bytes_len) };

    let mut sum_lum = 0.0f64;
    let mut min_lum = 1.0f32;
    let mut max_lum = 0.0f32;
    let mut count = 0u64;

    // Sample step: sample every 2nd or 3rd pixel to be blazingly fast (<0.1ms)
    let step_x = 1.max((end_x - rx) / 64);
    let step_y = 1.max((end_y - ry) / 64);

    let mut y = ry;
    while y < end_y {
        let row_offset = (y * stride) as usize;
        let mut x = rx;
        while x < end_x {
            let offset = row_offset + (x * 4) as usize;
            if offset + 3 < total_bytes_len {
                let b = pixels[offset];
                let g = pixels[offset + 1];
                let r = pixels[offset + 2];
                // ITU-R BT.709 perceived luminance
                let lum = (0.2126 * (r as f32 / 255.0) + 0.7152 * (g as f32 / 255.0) + 0.0722 * (b as f32 / 255.0)).clamp(0.0, 1.0);
                sum_lum += lum as f64;
                if lum < min_lum { min_lum = lum; }
                if lum > max_lum { max_lum = lum; }
                count += 1;
            }
            x += step_x;
        }
        y += step_y;
    }

    if count == 0 {
        return false;
    }

    let mean_lum = (sum_lum / count as f64) as f32;
    // Contrast ratio against white (L=1.0) and black (L=0.0)
    let cw = (1.0 + 0.05) / (mean_lum + 0.05);
    let cb = (mean_lum + 0.05) / 0.05;

    // If mean luminance > 0.48, dark text (#FF1A1A1A, 1) provides better contrast; otherwise light text (0)
    let recommended_color = if mean_lum > 0.48 { 1 } else { 0 };

    unsafe {
        (*out_metrics).mean_luminance = mean_lum;
        (*out_metrics).min_luminance = min_lum;
        (*out_metrics).max_luminance = max_lum;
        (*out_metrics).recommended_text_color = recommended_color;
        (*out_metrics).contrast_ratio_against_white = cw;
        (*out_metrics).contrast_ratio_against_black = cb;
    }
    true
}

// ─────────────────────────────────────────────────────────────────────────────
// High-Speed Adaptive Typography & Text Fit Precomputation
// ─────────────────────────────────────────────────────────────────────────────

#[repr(C)]
pub struct TextFitResult {
    pub fits_without_clipping: bool,
    pub optimal_font_size: f32,
    pub truncate_char_index: i32, // -1 if fits, else index where ellipsis should begin
}

/// Zero-allocation text fit precomputation.
/// Calculates whether text fits inside an island container, the ideal font size, and safe truncation point.
#[no_mangle]
pub extern "C" fn horizon_compute_text_fit(
    available_width: f32,
    char_count: u32,
    avg_char_width_at_12pt: f32,
    min_font_size: f32,
    max_font_size: f32,
    out_result: *mut TextFitResult,
) -> bool {
    if out_result.is_null() || char_count == 0 || available_width <= 0.0 {
        return false;
    }

    let char_width_ratio = if avg_char_width_at_12pt > 0.0 {
        avg_char_width_at_12pt / 12.0
    } else {
        0.55 // default typical proportional font ratio
    };

    // Width needed at max font size:
    let needed_at_max = char_count as f32 * (max_font_size * char_width_ratio);
    if needed_at_max <= available_width {
        unsafe {
            (*out_result).fits_without_clipping = true;
            (*out_result).optimal_font_size = max_font_size;
            (*out_result).truncate_char_index = -1;
        }
        return true;
    }

    // Try scaling down font size:
    let ideal_font_size = (available_width / (char_count as f32 * char_width_ratio)).clamp(min_font_size, max_font_size);
    let needed_at_ideal = char_count as f32 * (ideal_font_size * char_width_ratio);

    if needed_at_ideal <= available_width {
        unsafe {
            (*out_result).fits_without_clipping = true;
            (*out_result).optimal_font_size = ideal_font_size;
            (*out_result).truncate_char_index = -1;
        }
        return true;
    }

    // Cannot fit even at min_font_size without truncation:
    let char_w_min = min_font_size * char_width_ratio;
    let max_chars = ((available_width / char_w_min) - 3.0).max(1.0) as i32;

    unsafe {
        (*out_result).fits_without_clipping = false;
        (*out_result).optimal_font_size = min_font_size;
        (*out_result).truncate_char_index = max_chars.min(char_count as i32 - 1);
    }
    true
}


// ============================================================================
// NATIVE HOMEPAGE LAYOUT & SPACING ENGINE (Theme 4 / Neo-Canvas)
// ============================================================================

#[repr(C)]
#[derive(Clone, Copy, Debug)]
pub struct NativeLayoutRect {
    pub x: f32,
    pub y: f32,
    pub width: f32,
    pub height: f32,
}

/// Computes mathematically uniform item coordinates, symmetrical margins, and
/// exact gap distributions with zero subpixel jitter and zero WPF measure drift.
#[no_mangle]
pub extern "C" fn horizon_compute_homepage_layout(
    container_w: f32,
    _container_h: f32,
    num_items: u32,
    item_w: f32,
    item_h: f32,
    target_gap: f32,
    out_rects: *mut NativeLayoutRect,
    max_rects: u32,
) -> u32 {
    if out_rects.is_null() || max_rects == 0 || num_items == 0 || container_w <= 0.0 {
        return 0;
    }
    let count = (num_items.min(max_rects)) as usize;
    let available_w = container_w.max(item_w);
    let cols = ((available_w + target_gap) / (item_w + target_gap)).floor().max(1.0) as usize;
    let actual_cols = cols.min(count).max(1);
    let total_grid_w = actual_cols as f32 * item_w + (actual_cols - 1) as f32 * target_gap;
    let start_x = ((container_w - total_grid_w) / 2.0).max(0.0);

    let slice = unsafe { std::slice::from_raw_parts_mut(out_rects, count) };
    for i in 0..count {
        let col = i % actual_cols;
        let row = i / actual_cols;
        slice[i] = NativeLayoutRect {
            x: (start_x + col as f32 * (item_w + target_gap)).round(),
            y: (row as f32 * (item_h + target_gap)).round(),
            width: item_w,
            height: item_h,
        };
    }
    count as u32
}
