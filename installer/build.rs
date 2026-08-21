fn main() {
    let is_windows = std::env::var("CARGO_CFG_TARGET_OS").unwrap_or_default() == "windows";
    let require_admin = std::env::var("CARGO_FEATURE_REQUIRE_ADMIN").is_ok();
    if is_windows && require_admin {
        let _ = embed_resource::compile("app.rc", embed_resource::NONE);
    }
}
