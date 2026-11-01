const {test}=require('node:test'),assert=require('node:assert/strict');
const {AdminSession,WriteGuard,snapshot,storage,auth,copy,vnDateTime}=require('./f67-helpers.cjs');
function setup(module='categories',role='Admin'){
 const calls=[],items=[{id:1,name:'Cơm',description:'',displayOrder:1,isActive:true}],a=auth(role),store=storage();
 const api={baseUrl:'/api',get:async p=>{calls.push(['get',p]);return copy(p.startsWith('/categories')?items:p.startsWith('/management/promotions')?[{id:1,name:'KM',isActive:true}]:p==='/management/settings'?{id:0,restaurantName:'Quán',currency:'VND'}:[]);}};
 for(const verb of ['post','put','patch','delete'])api[verb]=async(p,b)=>{calls.push([verb,p,b]);};
 const s=new AdminSession({api,auth:a,storage:store});s.module=module;return {s,api,a,store,calls,items};
}
test('Phục vụ không được tải/ghi quản trị; kho không được mở thiết lập/khuyến mãi/khu vực',async()=>{
 for(const [m,r] of [['categories','Waiter'],['settings','WarehouseManager'],['promotions','WarehouseManager'],['areas','WarehouseManager']]){const x=setup(m,r);await assert.rejects(()=>x.s.refresh(),e=>e.status===403);assert.equal(x.calls.length,0);}
});
test('Sửa danh mục giữ isActive hiện tại, không vô tình ngừng danh mục vì DTO mặc định false',async()=>{
 const x=setup();await x.s.refresh();await x.s.command('update',1,{name:'Cơm mới',description:'',displayOrder:2},snapshot(x.s.items[0]));
 assert.equal(x.calls.find(c=>c[0]==='put')[2].isActive,true);
});
test('Snapshot thay đổi giữa mở form và lưu thì không gửi PUT',async()=>{
 const x=setup();await x.s.refresh();const expected=snapshot(x.s.items[0]);x.items[0].name='Người khác đã sửa';
 await assert.rejects(()=>x.s.command('update',1,{name:'Cũ',description:'',displayOrder:0},expected),e=>e.status===409);assert.ok(x.calls.every(c=>c[0]==='get'));
});
test('Bật khuyến mãi gửi active trong query, không body mơ hồ',async()=>{
 const x=setup('promotions');await x.s.refresh();await x.s.command('active',1,{isActive:true},snapshot(x.s.items[0]));
 assert.deepEqual(x.calls.find(c=>c[0]==='patch'),['patch','/management/promotions/1/active?active=true',undefined]);
});
test('Lưu thiết lập dùng PUT singleton và chỉ gửi trường được phép',async()=>{
 const x=setup('settings');await x.s.refresh();await x.s.command('update',0,{restaurantName:'Quán',address:'',phone:'',taxCode:'',currency:'VND',taxRate:10,taxIncluded:true,serviceChargeRate:5,roles:['Admin']},snapshot(x.s.items[0]));
 const c=x.calls.find(c=>c[0]==='put');assert.equal(c[1],'/management/settings');assert.equal(c[2].roles,undefined);assert.equal(c[2].taxIncluded,true);
});
test('Chặn phần trăm, thời gian khuyến mãi sai và tiền âm trước khi ghi',async()=>{
 const x=setup('promotions');await x.s.refresh();const b={name:'KM',description:'',discountType:'Percent',discountValue:101,startAt:'2026-11-01T10:00',endAt:'2026-11-02T10:00',minimumOrderAmount:0,maxDiscountAmount:0};
 await assert.rejects(()=>x.s.command('create',null,b),e=>e.status===400);b.discountValue=10;b.endAt=b.startAt;await assert.rejects(()=>x.s.command('create',null,b),e=>e.status===400);assert.ok(x.calls.every(c=>c[0]==='get'));
});
test('Không được gọi hành động topping/default sai loại danh mục',async()=>{
 const x=setup();await x.s.refresh();await assert.rejects(()=>x.s.command('availability',1,{isAvailable:true},snapshot(x.s.items[0])),e=>e.status===400);assert.ok(x.calls.every(c=>c[0]==='get'));
});
test('Mất ACK lưu pending, reload chỉ GET để đối chiếu, không tự gửi lại POST',async()=>{
 const x=setup();await x.s.refresh();x.api.post=async(p,b)=>{x.calls.push(['post',p,b]);throw new Error('Mất ACK');};
 await assert.rejects(()=>x.s.command('create',null,{name:'Mới',description:'',displayOrder:0}));assert.ok(x.s.pending);
 const restored=new AdminSession({api:x.api,auth:x.a,storage:x.store});await restored.review(()=>restored.load());restored.acknowledge();assert.equal(x.calls.filter(c=>c[0]==='post').length,1);assert.equal(restored.pending,null);
});
test('Có ACK nhưng GET thất bại vẫn giữ accepted và chặn lệnh tiếp theo',async()=>{
 const x=setup();await x.s.refresh();x.api.put=async()=>{x.api.get=async()=>{throw Object.assign(new Error('500'),{status:500});};};
 await assert.rejects(()=>x.s.command('update',1,{name:'Mới',description:'',displayOrder:0},snapshot(x.s.items[0])));assert.equal(x.s.pending.accepted,true);
 await assert.rejects(()=>x.s.command('update',1,{name:'Khác',description:'',displayOrder:0},snapshot(x.s.items[0])));
});
test('WriteGuard chặn bấm kép và đổi tài khoản trong khi preflight',async()=>{
 const a=auth(),g=new WriteGuard({api:{baseUrl:'/api'},auth:a,storage:storage(),key:'k'});let release,writes=0;
 const p=g.write('Lưu',()=>new Promise(r=>release=r),async()=>writes++,async()=>{});
 await assert.rejects(()=>g.write('Lưu',async()=>{},async()=>writes++,async()=>{}));a.userId=10;release();await assert.rejects(()=>p,e=>e.status===401);assert.equal(writes,0);
});
test('Pending không đi sang user hoặc API khác; refresh pending không tự đánh dấu đã đối chiếu',()=>{
 const st=storage();st.setItem('k',JSON.stringify({userId:9,realm:'/other',reviewed:true}));const g=new WriteGuard({api:{baseUrl:'/api'},auth:auth(),storage:st,key:'k'});assert.equal(g.pending,null);
 st.setItem('k',JSON.stringify({userId:9,realm:'/api',reviewed:true}));const r=new WriteGuard({api:{baseUrl:'/api'},auth:auth(),storage:st,key:'k'});assert.equal(r.pending.reviewed,false);assert.throws(()=>r.acknowledge());
});
test('Ngày khuyến mãi không được tự cuộn 30/2 sang tháng sau; giữ giây và giờ Việt Nam',()=>{
 assert.throws(()=>vnDateTime('2026-02-30T10:00:00'),e=>e.status===400);
 assert.throws(()=>vnDateTime('2026-11-01T25:00'),e=>e.status===400);
 assert.equal(new Date(vnDateTime('2026-11-01T17:42:56')).toISOString(),'2026-11-01T10:42:56.000Z');
});
test('Kích cỡ tên riêng đang tồn tại vẫn sửa được, không ép đổi thành Nhỏ/Vừa/Lớn',()=>{
 const x=setup('sizes');const body=x.s.body({sizeName:'Phần đặc biệt',sizePrice:25000,isDefault:false});assert.equal(body.sizeName,'Phần đặc biệt');assert.equal(body.isDefault,false);
});
test('Mất ACK gán topping giữ đúng module và foodId khi mở lại',async()=>{
 const x=setup('links');x.s.foodId=20;
 x.api.get=async p=>p.startsWith('/foods?')?[{foodId:20,foodName:'Món',isActive:true}]:p.startsWith('/toppings?')?[{toppingId:2,isActive:true}]:[];
 x.api.post=async()=>{throw new Error('ACK');};await x.s.refresh();await assert.rejects(()=>x.s.command('create',null,{toppingId:2}));
 const restored=new AdminSession({api:x.api,auth:x.a,storage:x.store});assert.equal(restored.module,'links');assert.equal(restored.foodId,20);
});
test('Lưu bàn không thay đổi chỉ đọc đối chiếu, không PUT rồi nhận 404 do số dòng sửa bằng 0',async()=>{
 const x=setup('tables'),table={tableId:5,tableNumber:'05',capacity:4,areaId:null,status:'Available',isActive:true};
 x.api.get=async p=>p.startsWith('/table-areas')?[]:[copy(table)];await x.s.refresh();const result=await x.s.command('update',5,{tableNumber:'05',capacity:4,areaId:''},snapshot(x.s.items[0]));
 assert.equal(result.unchanged,true);assert.ok(!x.calls.some(c=>c[0]==='put'));assert.equal(x.s.pending,null);
});
