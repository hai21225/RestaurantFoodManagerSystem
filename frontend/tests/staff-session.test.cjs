const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');
const source = fs.readFileSync(require('node:path').join(__dirname, '../js/common/staff-session.js'), 'utf8');
const clean = (v) => JSON.parse(JSON.stringify(v));
const err = (status, message) => Object.assign(new Error(message), {status});
function fixture(role = 'Admin') {
    const context = {window:{}, TextEncoder}; vm.runInNewContext(source, context);
    const lib = context.window.restaurantStaff;
    const roles = ['Admin','Waiter','Chef','Cashier','WarehouseManager'].map((roleName,i)=>({roleId:i+1,roleName,roleDescription:roleName}));
    const user = (userId, name, roles, isActive=true) => ({userId,userName:name,fullName:name,email:`${name}@example.invalid`,phone:'0000000000',roles,isActive,updatedAt:'2026-11-01T00:00:00Z'});
    const users = [user(1,'admin',['Admin']),user(2,'staff',['Waiter']),user(3,'chef',['Chef']),user(4,'locked-admin',['Admin'],false)];
    const calls=[], saved=new Map();
    const storage = {getItem:k=>saved.get(k)||null,setItem:(k,v)=>saved.set(k,v),removeItem:k=>saved.delete(k)};
    let actor = {userId:1,roles:[role]}, extra = null, revision=0;
    const auth = {getUser:()=>actor,hasRole:(...r)=>actor.roles.some(x=>r.includes(x))};
    const find = (id) => {const u=users.find(u=>u.userId===Number(id));if(!u)throw err(404,'User not found');return u;};
    const api={};
    for(const method of ['get','post','put','patch','delete']) api[method] = async(path,body)=>{
        calls.push({method,path,body:body===undefined?undefined:clean(body)});
        if(extra){ const response=await extra(method,path,body); if(response?.handled)return response.value; }
        const url=new URL(path,'http://test'), id=url.pathname.split('/')[2];
        if(method==='get'){
            if(path==='/roles')return clean(roles);
            if(path==='/users')return clean(users);
            return clean(find(id));
        }
        if(method==='post' && path==='/users'){users.push(user(5,body.userName,body.roleName?[body.roleName]:[]));return {message:'Employee created successfully'};}
        const target=find(id);
        if(method==='put')Object.assign(target,body);
        else if(method==='patch'){assert.equal(body,undefined);target.isActive=url.searchParams.get('active')==='true';}
        else if(method==='delete')target.roles=target.roles.filter(r=>r!==decodeURIComponent(url.pathname.split('/')[4]));
        else if(path.endsWith('/roles'))target.roles.push(body.roleName);
        target.updatedAt=`2026-11-01T00:00:${String(++revision).padStart(2,'0')}Z`;
        return path.endsWith('/roles')||path.endsWith('/reset-password')||method==='delete'?{message:'OK'}:clean(target);
    };
    const session = new lib.StaffSession({api,auth,storage});
    return {lib,session,api,users,roles,calls,storage,saved,find,setActor:v=>actor=v,hook:v=>extra=v};
}
const create = {fullName:'Người thử',email:'new@example.invalid',phone:'0000000000',userName:'new',password:'Only-Test-123',confirmPassword:'Only-Test-123',roleName:'Waiter'};
const writes = (f) => f.calls.filter(c=>c.method!=='get');
async function ready(f=fixture()){await f.session.refresh();f.calls.length=0;return f;}
test('chỉ Admin được đọc/ghi nhân viên và đọc vai trò, vai trò khác không gửi HTTP',async()=>{
    for(const role of ['Waiter','Chef','Cashier','WarehouseManager']){const f=fixture(role);await assert.rejects(f.session.refresh(),e=>e.status===403);await assert.rejects(f.session.loadRoles(),e=>e.status===403);await assert.rejects(f.session.command('create',null,create),e=>e.status===403);assert.equal(f.calls.length,0);}
});
test('list là mảng và 5 vai trò lấy nguyên từ API; lỗi schema không hiện dữ liệu đã tải',async()=>{
    const f=await ready();assert.equal(f.session.roles.length,5);assert.equal(f.session.users.length,4);
    f.hook(async(m,p)=>p==='/users'?{handled:true,value:{items:f.users}}:null);
    await assert.rejects(f.session.refresh(),e=>e.status===502);assert.equal(f.session.loaded,false);
});
test('detail null hoặc sai ID bị chặn, không biến thành thông tin nhân viên khác',async()=>{
    const f=await ready();f.hook(async()=>({handled:true,value:null}));await assert.rejects(f.session.userDetails(2),e=>e.status===502);
    f.hook(async()=>({handled:true,value:f.users[0]}));await assert.rejects(f.session.userDetails(2),e=>e.status===502);
});
test('tạo có DTO sạch, vai trò lấy từ API, không gửi actor/isActive/roles tự chèn',async()=>{
    const f=await ready();await f.session.command('create',null,{...create,fullName:' Người thử ',actorId:9,isActive:false,roles:['Admin']});
    assert.deepEqual(writes(f)[0],{method:'post',path:'/users',body:{fullName:'Người thử',email:create.email,phone:create.phone,userName:'new',password:create.password,confirmPassword:create.password,roleName:'Waiter'}});assert.equal(f.session.pending,null);
});
test('tạo chưa gán vai trò gửi null, không tự cấp Admin',async()=>{
    const f=await ready();await f.session.command('create',null,{...create,roleName:''});assert.equal(writes(f)[0].body.roleName,null);assert.equal(f.find(5).roles.length,0);
});
test('họ tên/email/điện thoại và tên đăng nhập sai chặn trước ghi',async()=>{
    for(const data of [{fullName:''},{fullName:'a'.repeat(151)},{email:'invalid'},{email:'a'.repeat(151)+'@x'},{phone:'1'.repeat(21)},{userName:''},{userName:'a'.repeat(101)},{roleName:'Manager'}]){
        const f=await ready();await assert.rejects(f.session.command('create',null,{...create,...data}),e=>e.status===400);assert.equal(writes(f).length,0);
    }
});
test('mật khẩu tính byte UTF-8, giữ khoảng trắng thật, xác nhận phải trùng',()=>{
    const f=fixture();for(const data of [{newPassword:'12345',confirmPassword:'12345'},{newPassword:'á'.repeat(37),confirmPassword:'á'.repeat(37)},{newPassword:'123456',confirmPassword:'123457'},{newPassword:'      ',confirmPassword:'      '}])assert.throws(()=>f.lib.passwordBody(data),e=>e.status===400);
    assert.equal(f.lib.passwordBody({newPassword:'á'.repeat(36),confirmPassword:'á'.repeat(36)}).newPassword.length,36);
    assert.equal(f.lib.passwordBody({newPassword:' 1234 ',confirmPassword:' 1234 '}).newPassword,' 1234 ');
});
test('PUT chỉ sửa liên hệ, giữ username, trạng thái và nhiều vai trò',async()=>{
    const f=await ready();f.find(2).roles.push('Chef');const u=await f.session.userDetails(2);
    await f.session.command('update',2,{...create,fullName:'Tên mới',userName:'hijack',roles:['Admin'],isActive:false},f.lib.snapshot(u));
    assert.deepEqual(writes(f)[0].body,{fullName:'Tên mới',email:create.email,phone:create.phone});assert.equal(f.find(2).userName,'staff');assert.deepEqual(f.find(2).roles,['Waiter','Chef']);
});
test('khóa/mở dùng PATCH query active và không gửi body isActive',async()=>{
    const f=await ready();await f.session.command('active',2,{isActive:false});assert.equal(writes(f)[0].path,'/users/2/active?active=false');assert.equal(writes(f)[0].body,undefined);assert.equal(f.find(2).isActive,false);
    await f.session.command('active',2,{isActive:true});assert.equal(f.find(2).isActive,true);
});
test('chặn tự khóa và khóa Admin active cuối, không tính Admin bị khóa',async()=>{
    const f=await ready();await assert.rejects(f.session.command('active',1,{isActive:false}),/tự khóa/);assert.equal(writes(f).length,0);
    await f.session.refresh();f.setActor({userId:9,roles:['Admin']});await assert.rejects(f.session.command('active',1,{isActive:false}),/cuối/);assert.equal(writes(f).length,0);
});
test('có Admin active khác thì cho khóa target và mở lại, dùng trạng thái mới',async()=>{
    const f=await ready();f.setActor({userId:4,roles:['Admin']});f.find(4).isActive=true;await f.session.command('active',1,{isActive:false});assert.equal(f.find(1).isActive,false);await f.session.command('active',1,{isActive:true});assert.equal(f.find(1).isActive,true);
});
test('gán/thu từng vai trò giữ nguyên các vai trò còn lại và URI đúng',async()=>{
    const f=await ready();await f.session.command('assignRole',2,{roleName:'Chef'});assert.deepEqual(f.find(2).roles,['Waiter','Chef']);
    await f.session.command('removeRole',2,{roleName:'Waiter'});assert.deepEqual(f.find(2).roles,['Chef']);assert.equal(writes(f)[1].path,'/users/2/roles/Waiter');assert.equal(writes(f)[1].method,'delete');
});
test('chặn vai trò trùng, lạ, rỗng, không còn gán; không gửi lệnh',async()=>{
    for(const [kind,roleName] of [['assignRole','Waiter'],['assignRole','Manager'],['assignRole',''],['removeRole','Cashier']]){const f=await ready();await assert.rejects(f.session.command(kind,2,{roleName}));assert.equal(writes(f).length,0);}
});
test('không thu Admin cuối, tính theo tài khoản active khác',async()=>{
    const f=await ready();await assert.rejects(f.session.command('removeRole',1,{roleName:'Admin'}),/cuối/);assert.equal(writes(f).length,0);
    await f.session.refresh();f.find(4).isActive=true;await f.session.command('removeRole',1,{roleName:'Admin'});assert.deepEqual(f.find(1).roles,[]);assert.equal(f.session.needsLogin,true);
});
test('thao tác quyền của mình cần đăng nhập lại, quyền người khác không đăng xuất Admin',async()=>{
    const f=await ready();await f.session.command('assignRole',2,{roleName:'Cashier'});assert.equal(f.session.needsLogin,false);await f.session.command('assignRole',1,{roleName:'Waiter'});assert.equal(f.session.needsLogin,true);
});
test('đặt lại mật khẩu chỉ gửi newPassword/confirmPassword, tự đặt lại cần đăng nhập',async()=>{
    const f=await ready();const body={newPassword:'Reset-Test-123',confirmPassword:'Reset-Test-123',oldPassword:'secret',roleName:'Admin'};
    await f.session.command('resetPassword',2,body);assert.deepEqual(writes(f)[0].body,{newPassword:body.newPassword,confirmPassword:body.confirmPassword});assert.equal(f.session.needsLogin,false);
    await f.session.command('resetPassword',1,body);assert.equal(f.session.needsLogin,true);
});
test('snapshot phát hiện thay đổi liên hệ, quyền/trạng thái và cập nhật mật khẩu trước ghi',async()=>{
    for(const mutate of [u=>u.email='changed@example.invalid',u=>u.roles.push('Chef'),u=>u.isActive=false,u=>u.updatedAt='2026-11-01T01:00:00Z']){
        const f=await ready(),u=await f.session.userDetails(2),expected=f.lib.snapshot(u);mutate(f.find(2));await assert.rejects(f.session.command('update',2,create,expected),/vừa thay đổi/);assert.equal(writes(f).length,0);
    }
});
test('snapshot không coi thứ tự role là thay đổi',()=>{const f=fixture();assert.equal(f.lib.snapshot({...f.find(2),roles:['Waiter','Chef']}),f.lib.snapshot({...f.find(2),roles:['Chef','Waiter']}));});
test('bấm kép chỉ gửi một POST, chặn lệnh khi đang tải',async()=>{
    const f=await ready();let release;const blocked=new Promise(r=>release=r);f.hook(async(m)=>{if(m==='post')await blocked;});const first=f.session.command('create',null,create);await assert.rejects(f.session.command('create',null,create),/Chờ/);release();await first;assert.equal(writes(f).length,1);
});
test('mất ACK khi tạo giữ intent không chứa mật khẩu, reload chỉ đọc rồi ACK thủ công',async()=>{
    const f=await ready();f.hook(async(m)=>{if(m==='post')throw err(0,'Lost ACK');});await assert.rejects(f.session.command('create',null,create));const raw=f.storage.getItem(f.lib.pendingKey);assert.ok(raw);assert.ok(!raw.includes(create.password));assert.ok(!raw.includes('confirmPassword'));
    const restored=new f.lib.StaffSession({api:f.api,auth:{getUser:()=>({userId:1}),hasRole:()=>true},storage:f.storage});await restored.reviewPending();assert.equal(restored.pending.reviewed,true);restored.acknowledgeReview();assert.equal(restored.pending,null);assert.equal(writes(f).length,1);
});
test('mất ACK khi reset không lưu bất kỳ giá trị mật khẩu, không tự POST lại',async()=>{
    const f=await ready(),newPassword='Unique-Reset-TEST';f.hook(async(m)=>{if(m==='post')throw err(500,'Unknown');});await assert.rejects(f.session.command('resetPassword',2,{newPassword,confirmPassword:newPassword}));assert.ok(!f.storage.getItem(f.lib.pendingKey).includes(newPassword));await f.session.reviewPending();assert.equal(writes(f).length,1);
});
test('ACK thành công nhưng tải sau ghi lỗi: review chỉ đọc, không ghi lần hai',async()=>{
    const f=await ready();let written=false;f.hook(async(m,p)=>{if(m==='post')written=true;else if(p==='/users'&&written)throw err(503,'Read failed');});await assert.rejects(f.session.command('create',null,create));assert.equal(f.session.pending.accepted,true);f.hook(null);assert.equal(await f.session.reviewPending(),true);assert.equal(f.session.pending,null);assert.equal(writes(f).length,1);
});
test('400/403/404/409 giữ nguyên message và bỏ pending; 5xx vẫn khóa đối chiếu',async()=>{
    for(const status of [400,403,404,409,500]){const f=await ready();f.hook(async(m)=>{if(m==='post')throw err(status,'Exact server message');});await assert.rejects(f.session.command('create',null,create),e=>e.message==='Exact server message');assert.equal(!!f.session.pending,status===500);}
});
test('storage không ghi được thì không gửi mật khẩu hay lệnh lên API',async()=>{
    const f=await ready();f.storage.setItem=()=>{throw Error('storage full');};await assert.rejects(f.session.command('create',null,create),e=>e.status===400);assert.equal(writes(f).length,0);
});
test('intent gắn user, reload phải đối chiếu lại và không ACK trước khi review',async()=>{
    const f=await ready();f.session.pending={kind:'create',userId:1,id:null,targetName:'test',reviewed:true};f.session.savePending();const auth={getUser:()=>({userId:1}),hasRole:()=>true};const restored=new f.lib.StaffSession({api:f.api,auth,storage:f.storage});assert.equal(restored.pending.reviewed,false);assert.throws(()=>restored.acknowledgeReview());
    const other=new f.lib.StaffSession({api:f.api,auth:{...auth,getUser:()=>({userId:8})},storage:f.storage});assert.equal(other.pending,null);
});
test('đổi user giữa preflight không gửi lệnh, không review intent của người khác',async()=>{
    const f=await ready();f.hook(async(m,p)=>{if(p==='/roles')f.setActor({userId:8,roles:['Admin']});});await assert.rejects(f.session.command('create',null,create),e=>e.status===403);assert.equal(writes(f).length,0);
    f.session.pending={kind:'create',userId:1};await assert.rejects(f.session.reviewPending(),e=>e.status===403);
});
test('chi tiết đến trễ hoặc lỗi của lựa chọn cũ không ghi đè người mới chọn',async()=>{
    const f=await ready();let release;const wait=new Promise(r=>release=r);f.hook(async(m,p)=>{if(p==='/users/2')await wait;});const old=f.session.selectUser(2);await f.session.selectUser(3);release();await old;assert.equal(f.session.detail.userId,3);
    let reject;const later=new Promise((_,r)=>reject=r);f.hook(async(m,p)=>{if(p==='/users/2')await later;});const oldError=f.session.selectUser(2);await f.session.selectUser(3);reject(err(404,'old missing'));await oldError;assert.equal(f.session.detail.userId,3);assert.equal(f.session.detailLoading,false);
});
test('ID/loại lệnh sai hoặc chưa tải không gửi request ghi',async()=>{
    const f=fixture();await assert.rejects(f.session.command('update',2,create));await f.session.refresh();for(const [kind,id] of [['invalid',2],['update',true],['update',-1],['update',1.2],['create',2]])await assert.rejects(f.session.command(kind,id,create));assert.equal(writes(f).length,0);
});
